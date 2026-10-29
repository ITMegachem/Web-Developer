using Mgt.Lit.Core.DTOs;
using Mgt.Lit.WebFront.Auth;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net;
using System.Net.Http.Json;

namespace Mgt.Lit.WebFront.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly AuthState _authState;
    private readonly ProtectedSessionStorage _legacySessionStorage;   // เฉพาะล้าง login แบบเก่าที่ค้างใน session storage
    private readonly ProtectedLocalStorage _storage;                  // ★ login เก็บที่นี่ที่เดียว (ทุกแท็บใช้ร่วมกัน)
    private readonly SessionBroadcast _broadcast;

    public AuthService(HttpClient http, AuthState authState, ProtectedSessionStorage sessionStorage,
                       ProtectedLocalStorage localStorage, SessionBroadcast broadcast)
    {
        _http = http;
        _authState = authState;
        _legacySessionStorage = sessionStorage;
        _storage = localStorage;
        _broadcast = broadcast;
    }

    private static async Task ClearStorageAsync(ProtectedBrowserStorage storage)
    {
        foreach (var key in AuthState.StorageKeys)
        {
            try { await storage.DeleteAsync(key); } catch { /* best-effort */ }
        }
    }

    // ล้าง login ทั้งใน local storage และของเก่าที่อาจค้างใน session storage (ก่อนเปลี่ยนมาเก็บที่เดียว)
    private async Task ClearAllStoredLoginAsync()
    {
        await ClearStorageAsync(_storage);
        await ClearStorageAsync(_legacySessionStorage);
    }

    private async Task<string?> ReadStoredStringAsync(string key)
    {
        try
        {
            var r = await _storage.GetAsync<string>(key);
            if (r.Success && !string.IsNullOrWhiteSpace(r.Value)) return r.Value;
        }
        catch { }
        return null;
    }

    // ✅ ดึง refreshToken จาก Set-Cookie header แล้วเก็บใน storage (ใช้ทั้ง password login และ SSO)
    private async Task SaveRefreshTokenAsync(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return;
        var rtCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        if (rtCookie == null)
            return;
        var rt = rtCookie.Split('=')[1].Split(';')[0];
        await _storage.SetAsync("refreshToken", rt);
        Console.WriteLine("[AuthService] refreshToken saved to storage");
    }

    // เก็บข้อมูล login ทั้งชุด + session cookie แล้ว rebuild AuthState
    private async Task StoreLoginAsync(LoginResponseDto result, HttpResponseMessage response, bool rememberMe)
    {
        _authState.Token = result.Token;
        _authState.IsRemembered = rememberMe;

        await ClearAllStoredLoginAsync();   // กัน login เก่า (ของ user อื่น/แบบเก่า) ค้างปน

        await SaveRefreshTokenAsync(response);
        await _storage.SetAsync("authToken", result.Token!);
        await _storage.SetAsync("rememberMe", rememberMe);
        await _storage.SetAsync("UserID", result.UserID);
        await _storage.SetAsync("fullName", result.FullName ?? string.Empty);
        await _storage.SetAsync("username", result.Username ?? string.Empty);
        await _storage.SetAsync("userRole", result.UserRole ?? string.Empty);
        await _storage.SetAsync("division", result.Division ?? string.Empty);
        await _storage.SetAsync("primaryCompanyID", result.PrimaryCompanyID);
        await _storage.SetAsync("primaryCompanyCode", result.PrimaryCompanyCode ?? string.Empty);
        await _storage.SetAsync("companies", result.Companies);
        await _storage.SetAsync("permission", result.Permission);

        // ★ session cookie: บอกว่า browser รอบนี้ login แล้ว — แท็บใหม่เห็นร่วมกัน, หายเองตอนปิด browser
        await _authState.MarkBrowserSessionAsync();

        await _authState.InitializeAsync(force: true);
        _broadcast.Publish(result.UserID.ToString()); // other tabs/devices of this user: check now
    }

    // ✅ LoginAsync อันเดียว — รวมการเก็บ refreshToken ไว้แล้ว
    // rememberMe = true  -> login อยู่ข้ามการปิด browser
    // rememberMe = false -> login ใช้ได้ทุกแท็บจนกว่าจะปิด browser
    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto req, bool rememberMe = false)
    {
        var response = await _http.PostAsJsonAsync("api/member/login", req);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        await StoreLoginAsync(result, response, rememberMe);
        return result;
    }

    public async Task<RefreshResponseDto?> RefreshAsync()
    {
        // Blazor Server: this HttpClient runs on the SERVER, so the browser's refreshToken cookie never
        // reaches the API. Send the refresh token we stored at login in the body instead.
        var rt = await ReadStoredStringAsync("refreshToken");
        if (string.IsNullOrWhiteSpace(rt))
            return null;

        var response = await _http.PostAsJsonAsync("api/member/refresh-explicit", new { RefreshToken = rt });

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RefreshResponseDto>();

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        _authState.Token = result.Token;
        await _storage.SetAsync("authToken", result.Token);
        await _storage.SetAsync("currentCompanyID", result.CurrentCompanyId);

        await _authState.InitializeAsync(force: true);

        return result;
    }

    public async Task<SwitchCompanyResponseDto?> SwitchCompanyAsync(int companyId)
    {
        var response = await _http.PostAsJsonAsync("api/member/switch-company", new SwitchCompanyRequest
        {
            CompanyID = companyId
        });

        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SwitchCompanyResponseDto>();

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        _authState.Token = result.Token;
        await _storage.SetAsync("authToken", result.Token);
        await _storage.SetAsync("currentCompanyID", result.CurrentCompanyId);

        if (result.Permission != null)
        {
            await _storage.SetAsync("permission", new Mgt.Lit.Core.Entities.View_UserPermission
            {
                UserRole = result.Permission.UserRole,
                Department = result.Permission.Department,
                RoleKey = result.Permission.RoleKey,
                Tier = result.Permission.Tier,
                Page1Access = result.Permission.Page1Access,
                Page2Access = result.Permission.Page2Access,
                Page3Access = result.Permission.Page3Access,
                Page4Access = result.Permission.Page4Access,
                DataScope = result.Permission.DataScope,
                CanViewVendor = result.Permission.CanViewVendor,
                CanViewCost = result.Permission.CanViewCost,
                CanViewCustomer = result.Permission.CanViewCustomer
            });
        }

        await _authState.InitializeAsync(force: true);
        return result;
    }

    public async Task LogoutAsync()
    {
        // Read what the server needs BEFORE clearing anything.
        var rt = await ReadStoredStringAsync("refreshToken");
        var accessToken = _authState.Token;
        var userId = _authState.User?.FindFirst("UserID")?.Value;

        // 1) Clear this browser FIRST (local storage is shared by every tab) — a leftover refreshToken
        //    would silently sign the person straight back in on the next page.
        _authState.Reset();
        await ClearAllStoredLoginAsync();
        Console.WriteLine("[Logout] local session cleared");

        // 2) Revoke the refresh token on the API — best effort with a short timeout, so a slow API/DB never
        //    blocks signing out. Token goes in the body: this HttpClient runs on the server (no cookie).
        if (string.IsNullOrWhiteSpace(rt)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/member/logout")
            {
                Content = JsonContent.Create(new { RefreshToken = rt })
            };
            if (!string.IsNullOrWhiteSpace(accessToken))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _http.SendAsync(req, cts.Token);
            Console.WriteLine($"[Logout] API revoke: {(int)resp.StatusCode}");
        }
        catch (Exception ex) { Console.WriteLine("[Logout] API revoke failed: " + ex.Message); }
        finally
        {
            _broadcast.Publish(userId);   // the API bumped TokenVersion → other tabs end now, not in 15 s
        }
    }

    public async Task<string?> GetMicrosoftLoginUrlAsync()
    {
        var response = await _http.GetFromJsonAsync<MicrosoftLoginUrlResponse>
            ("api/member/microsoft-login-url");
        return response?.Url;
    }

    public class MicrosoftLoginUrlResponse
    {
        public string Url { get; set; } = "";
    }

    public async Task<LoginResponseDto?> MicrosoftCallbackAsync(string code)
    {
        var response = await _http.PostAsJsonAsync(
            "api/member/microsoft-callback",
            new { Code = code });

        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) return null;

        // ✅ CaseInsensitive แก้ปัญหา token vs Token
        var result = System.Text.Json.JsonSerializer.Deserialize<LoginResponseDto>(
            raw,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        // SSO ไม่มีช่อง Remember me → ใช้ได้ทุกแท็บจนกว่าจะปิด browser
        await StoreLoginAsync(result, response, rememberMe: false);
        return result;
    }
}
