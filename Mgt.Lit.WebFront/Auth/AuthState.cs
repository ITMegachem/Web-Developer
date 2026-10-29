using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth;

public class AuthState
{
    // ★ Login เก็บใน ProtectedLocalStorage ที่เดียว — ทุกแท็บของ browser เดียวกันใช้ร่วมกัน (เปิดแท็บใหม่ไม่ต้อง login ซ้ำ)
    //   "Remember me" ไม่ได้เปลี่ยนที่เก็บแล้ว แต่ใช้ session cookie (mgt_alive) เป็นตัวบอกว่า browser ยังเปิดอยู่:
    //   - ติ๊ก Remember me   → login อยู่ข้ามการปิด browser
    //   - ไม่ติ๊ก            → เปิด browser ใหม่แล้วไม่มี cookie = ปิด browser ไปแล้ว → ล้าง login ทิ้ง
    private readonly ProtectedLocalStorage _localStorage;
    private readonly IJSRuntime _js;
    private readonly IHttpClientFactory _factory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _isInitialized;

    /// ทุก key ของ login ใน local storage (AuthService ใช้ล้างตอน login/logout)
    public static readonly string[] StorageKeys =
    {
        "authToken", "refreshToken", "UserID", "fullName", "username", "userRole",
        "division", "primaryCompanyID", "primaryCompanyCode", "currentCompanyID", "companies", "permission",
        "rememberMe"
    };

    public string? Token { get; set; }
    public ClaimsPrincipal? User { get; set; }
    public View_UserPermission? Permission { get; set; }
    public string? ExportPassword { get; set; }
    public int? CurrentCompanyID { get; set; }
    public void Clear() { Token = null; User = null; }
    public bool? CanViewCost { get; set; }

    /// true เมื่อ login ปัจจุบันติ๊ก "Remember me" (อ่านจาก key "rememberMe" ใน local storage)
    public bool IsRemembered { get; set; }

    public AuthState(ProtectedLocalStorage localStorage, IJSRuntime js, IHttpClientFactory factory)
    {
        _localStorage = localStorage;
        _js = js;
        _factory = factory;
    }

    private async Task<(bool Success, T? Value)> GetAsync<T>(string key)
    {
        var r = await _localStorage.GetAsync<T>(key);
        return (r.Success, r.Value);
    }

    private Task SetAsync(string key, object value) => _localStorage.SetAsync(key, value).AsTask();

    private async Task ClearStoredLoginAsync()
    {
        foreach (var key in StorageKeys)
        {
            try { await _localStorage.DeleteAsync(key); } catch { /* best-effort */ }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Browser-session cookie (ไม่มีวันหมดอายุ → browser ลบเองตอนปิด, ทุกแท็บเห็นร่วมกัน)
    // ฟังก์ชัน JS อยู่ใน wwwroot/js/site.js (window.mgtSessionCookie)
    // ---------------------------------------------------------------------------------------------

    /// เรียกตอน login สำเร็จ — บอกว่า browser รอบนี้ login แล้ว
    public async Task MarkBrowserSessionAsync()
    {
        try { await _js.InvokeVoidAsync("mgtSessionCookie.set"); }
        catch (JSException ex) { Console.WriteLine("[AuthState] set session cookie failed: " + ex.Message); }
    }

    private async Task<bool> HasBrowserSessionAsync()
    {
        try { return await _js.InvokeAsync<bool>("mgtSessionCookie.has"); }
        catch (JSException ex)
        {
            // JS ยังไม่โหลด/หาฟังก์ชันไม่เจอ → อย่าเตะคนออกเพราะเหตุนี้
            Console.WriteLine("[AuthState] read session cookie failed: " + ex.Message);
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Valid-token helpers. The login lives in localStorage (shared by every tab), so a tab can open
    // with a token that expired while it was idle or the browser was closed. Callers ask for a VALID
    // token: expired/expiring → refresh first (one refresh at a time per circuit). After a 401, pass
    // the rejected token to force a refresh unless another call already replaced it.
    // ---------------------------------------------------------------------------------------------
    public async Task<string?> GetValidTokenAsync(string? rejectedToken = null, CancellationToken ct = default)
    {
        if (!_isInitialized) await InitializeAsync();
        if (IsUsable(Token, rejectedToken)) return Token;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (IsUsable(Token, rejectedToken)) return Token;   // refreshed by a concurrent caller

            // ถ้า Initialize เพิ่งล้าง login ทิ้ง (ปิด browser แล้วไม่ได้ Remember me) จะไม่มี refreshToken → ไม่ refresh
            string? rt = null;
            try { var r = await GetAsync<string>("refreshToken"); rt = r.Success ? r.Value : null; } catch { }
            if (string.IsNullOrWhiteSpace(rt)) return null;

            var client = _factory.CreateClient("RefreshClient");
            using var resp = await client.PostAsJsonAsync("api/member/refresh-explicit", new { RefreshToken = rt }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[AuthState] Refresh failed: {(int)resp.StatusCode}");
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<RefreshBody>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(body?.Token)) return null;

            await SetAsync("authToken", body.Token);
            if (body.CurrentCompanyId != null) await SetAsync("currentCompanyID", body.CurrentCompanyId);
            await InitializeAsync(force: true);   // rebuild claims from the new token
            return Token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// Raised once when the session can't continue (token rejected and refresh failed — typically the
    /// account signed in on another device, which bumps TokenVersion and revokes refresh tokens).
    /// AppLayout shows the "session ended" popup.
    public event Action? SessionEnded;
    private bool _sessionEndedRaised;

    private HttpResponseMessage EndSession()
    {
        if (!_sessionEndedRaised)
        {
            _sessionEndedRaised = true;
            try { SessionEnded?.Invoke(); } catch { }
        }
        // A plain 401 instead of throwing: an exception out of a component lifecycle method kills the
        // whole Blazor circuit (every button stops working). Callers treat 401 as "no data".
        return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
    }

    /// Send with a valid token; on 401 refresh once and retry. <paramref name="build"/> must create a
    /// NEW request each call (a request can only be sent once). Never throws for auth problems:
    /// returns 401 and raises <see cref="SessionEnded"/>.
    public async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<string, HttpRequestMessage> build, CancellationToken ct = default)
    {
        var token = await GetValidTokenAsync(ct: ct);
        if (string.IsNullOrWhiteSpace(token)) return EndSession();
        using (var first = build(token))
        {
            var resp = await http.SendAsync(first, ct);
            if (resp.StatusCode != System.Net.HttpStatusCode.Unauthorized) return resp;
            resp.Dispose();
        }
        var fresh = await GetValidTokenAsync(rejectedToken: token, ct: ct);
        if (string.IsNullOrWhiteSpace(fresh)) return EndSession();
        using var retry = build(fresh);
        var second = await http.SendAsync(retry, ct);
        if (second.StatusCode == System.Net.HttpStatusCode.Unauthorized) { second.Dispose(); return EndSession(); }
        return second;
    }

    private static bool IsUsable(string? token, string? rejected) =>
        !string.IsNullOrWhiteSpace(token) && token != rejected && !IsExpiring(token);

    private static bool IsExpiring(string token)
    {
        try
        {
            var exp = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo; // UTC; MinValue if no exp
            return exp == DateTime.MinValue || exp <= DateTime.UtcNow.AddSeconds(30);
        }
        catch { return true; }
    }

    private sealed class RefreshBody
    {
        public string? Token { get; set; }
        public int? CurrentCompanyId { get; set; }
    }

    public async Task InitializeAsync(bool force = false)
    {
        if (_isInitialized && !force)
            return;

        _isInitialized = false;
        Token = null;
        User = null;
        Permission = null;
        CurrentCompanyID = null;
        IsRemembered = false;

        try
        {
            var tokenResult = await GetAsync<string>("authToken");
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Value))
            {
                _isInitialized = true;
                return;
            }

            // ไม่มี key "rememberMe" = login ที่เก็บไว้ก่อนอัปเดตนี้ (เดิมเก็บใน local storage = จำไว้แล้ว)
            // → ถือว่า remembered ไม่เตะออก
            var rememberResult = await GetAsync<bool>("rememberMe");
            IsRemembered = !rememberResult.Success || rememberResult.Value;

            // ไม่ได้ Remember me และไม่มี session cookie = browser ถูกปิดไปแล้วตั้งแต่ login → ล้าง login
            // (แท็บใหม่ใน browser เดิมยังเห็น cookie → ใช้ login เดิมต่อได้)
            if (!IsRemembered && !await HasBrowserSessionAsync())
            {
                Console.WriteLine("[AuthState] Browser was closed since a non-remembered login → signed out");
                await ClearStoredLoginAsync();
                _isInitialized = true;
                return;
            }

            Token = tokenResult.Value;
            if (force) _sessionEndedRaised = false;   // login ใหม่ / refresh สำเร็จ → popup "Session Ended" แสดงได้อีก

            var fullNameResult = await GetAsync<string>("fullName");
            var fullName = fullNameResult.Success ? (fullNameResult.Value ?? "") : "";

            var permissionResult = await GetAsync<View_UserPermission>("permission");
            Permission = permissionResult.Success ? permissionResult.Value : null;

            var companyResult = await GetAsync<int?>("currentCompanyID");
            CurrentCompanyID = companyResult.Success ? companyResult.Value : null;

            if (CurrentCompanyID == null)
            {
                var primaryCompanyResult = await GetAsync<int?>("primaryCompanyID");
                CurrentCompanyID = primaryCompanyResult.Success ? primaryCompanyResult.Value : null;
            }

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(Token);
            var claims = jwt.Claims.ToList();

            claims.RemoveAll(x => x.Type == "FullName");
            if (!string.IsNullOrWhiteSpace(fullName))
                claims.Add(new Claim("FullName", fullName));

            if (Permission != null)
            {
                claims.RemoveAll(x =>
                    x.Type == "Page1Access" ||
                    x.Type == "Page2Access" ||
                    x.Type == "Page3Access" ||
                    x.Type == "DataScope" ||
                    x.Type == "CanViewVendor" ||
                    x.Type == "CanViewCost");

                claims.Add(new Claim("Page1Access", Permission.Page1Access.ToString()));
                claims.Add(new Claim("Page2Access", Permission.Page2Access.ToString()));
                claims.Add(new Claim("Page3Access", Permission.Page3Access.ToString()));
                claims.Add(new Claim("DataScope", Permission.DataScope ?? ""));
                claims.Add(new Claim("CanViewVendor", Permission.CanViewVendor.ToString()));
                claims.Add(new Claim("CanViewCost", Permission.CanViewCost.ToString()));
            }

            if (CurrentCompanyID != null)
            {
                claims.RemoveAll(x => x.Type == "CompanyID");
                claims.Add(new Claim("CompanyID", CurrentCompanyID.Value.ToString()));
            }

            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Stored values can't be read (e.g. Data Protection keys changed after a redeploy, or a
            // corrupted entry) → treat as signed out instead of crashing the page.
            Console.WriteLine("[AuthState] Stored session unreadable → signed out: " + ex.Message);
            Token = null; User = null; Permission = null; CurrentCompanyID = null; IsRemembered = false;
            await ClearStoredLoginAsync();
        }
        finally
        {
            _isInitialized = true;
        }
    }

    public void Reset()
    {
        _isInitialized = false;
        Token = null;
        User = null;
        Permission = null;
        CurrentCompanyID = null;
        IsRemembered = false;
    }
}
