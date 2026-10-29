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
    private readonly ProtectedSessionStorage _storage;
    private readonly ProtectedLocalStorage _localStorage;
    private readonly SessionBroadcast _broadcast;

    public AuthService(HttpClient http, AuthState authState, ProtectedSessionStorage storage,
                       ProtectedLocalStorage localStorage, SessionBroadcast broadcast)
    {
        _http = http;
        _authState = authState;
        _storage = storage;
        _localStorage = localStorage;
        _broadcast = broadcast;
    }

    private static readonly string[] AuthStorageKeys =
    {
        "authToken", "refreshToken", "UserID", "fullName", "username", "userRole",
        "division", "primaryCompanyID", "primaryCompanyCode", "currentCompanyID", "companies", "permission"
    };

    // ★ ล้าง key เดิมของ storage อีกฝั่ง กัน remembered session เก่าค้างอยู่ตอนสลับไป/กลับจาก "Remember me"
    private async Task ClearStorageAsync(ProtectedBrowserStorage storage)
    {
        foreach (var key in AuthStorageKeys)
        {
            try { await storage.DeleteAsync(key); } catch { /* best-effort */ }
        }
    }

    // อ่านค่าจาก session storage ก่อน แล้วค่อย fallback ไป local storage (remembered login)
    private async Task<string?> ReadStoredStringAsync(string key)
    {
        try
        {
            var s = await _storage.GetAsync<string>(key);
            if (s.Success && !string.IsNullOrWhiteSpace(s.Value)) return s.Value;
        }
        catch { }
        try
        {
            var l = await _localStorage.GetAsync<string>(key);
            if (l.Success && !string.IsNullOrWhiteSpace(l.Value)) return l.Value;
        }
        catch { }
        return null;
    }

    // ✅ ดึง refreshToken จาก Set-Cookie header แล้วเก็บใน storage ที่ระบุ (ใช้ทั้ง password login และ SSO)
    private async Task SaveRefreshTokenAsync(HttpResponseMessage response, ProtectedBrowserStorage storage)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return;
        var rtCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        if (rtCookie == null)
            return;
        var rt = rtCookie.Split('=')[1].Split(';')[0];
        await storage.SetAsync("refreshToken", rt);
        Console.WriteLine("[AuthService] refreshToken saved to storage");
    }

    // ✅ LoginAsync อันเดียว — รวมการเก็บ refreshToken ไว้แล้ว
    // rememberMe = true -> เก็บ session ใน ProtectedLocalStorage (คงอยู่ข้ามการปิด browser)
    // rememberMe = false (ค่าเริ่มต้น) -> เก็บใน ProtectedSessionStorage เหมือนเดิม (หายเมื่อปิด browser)
    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto req, bool rememberMe = false)
    {
        var response = await _http.PostAsJsonAsync("api/member/login", req);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        _authState.Token = result.Token;
        _authState.IsRemembered = rememberMe;

        ProtectedBrowserStorage activeStorage = rememberMe ? _localStorage : _storage;
        ProtectedBrowserStorage inactiveStorage = rememberMe ? _storage : _localStorage;
        await ClearStorageAsync(inactiveStorage);

        await SaveRefreshTokenAsync(response, activeStorage);

        await activeStorage.SetAsync("authToken", result.Token);
        await activeStorage.SetAsync("UserID", result.UserID);
        await activeStorage.SetAsync("fullName", result.FullName ?? string.Empty);
        await activeStorage.SetAsync("username", result.Username ?? string.Empty);
        await activeStorage.SetAsync("userRole", result.UserRole ?? string.Empty);
        await activeStorage.SetAsync("division", result.Division ?? string.Empty);
        await activeStorage.SetAsync("primaryCompanyID", result.PrimaryCompanyID);
        await activeStorage.SetAsync("primaryCompanyCode", result.PrimaryCompanyCode ?? string.Empty);
        await activeStorage.SetAsync("companies", result.Companies);
        await activeStorage.SetAsync("permission", result.Permission);

        await _authState.InitializeAsync(force: true);
        _broadcast.Publish(result.UserID.ToString()); // other tabs/devices of this user: check now
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
        // ★ เขียนกลับไปที่ storage เดียวกับตอน login (remembered -> local, ไม่งั้น -> session) กัน remembered
        //   session ถูก "ลดระดับ" เป็น session-only ทุกครั้งที่ token refresh
        ProtectedBrowserStorage activeStorage = _authState.IsRemembered ? _localStorage : _storage;
        await activeStorage.SetAsync("authToken", result.Token);
        await activeStorage.SetAsync("currentCompanyID", result.CurrentCompanyId);

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
        ProtectedBrowserStorage activeStorage = _authState.IsRemembered ? _localStorage : _storage;
        await activeStorage.SetAsync("authToken", result.Token);
        await activeStorage.SetAsync("currentCompanyID", result.CurrentCompanyId);

        if (result.Permission != null)
        {
            await activeStorage.SetAsync("permission", new Mgt.Lit.Core.Entities.View_UserPermission
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
        // Read what the server needs BEFORE clearing anything (refreshToken อาจอยู่ session หรือ local).
        var rt = await ReadStoredStringAsync("refreshToken");
        var accessToken = _authState.Token;
        var userId = _authState.User?.FindFirst("UserID")?.Value;

        // 1) Clear this browser FIRST. ล้างทั้งสอง storage เสมอ ไม่ว่า session นี้จะ "Remember me" ไว้หรือไม่
        //    — กันกรณี logout แล้วยังมี remembered session เก่าเหลือใน local storage ทำให้ auto-login กลับมาอีกรอบ
        //    (ClearStorageAsync ลบทีละ key แบบ best-effort: ถ้า key หนึ่งพลาด ตัวอื่นก็ยังถูกลบ)
        _authState.Reset();
        await ClearStorageAsync(_storage);
        await ClearStorageAsync(_localStorage);
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

        // SSO ใช้ session storage (ไม่มี Remember me) — ล้าง remembered session เก่าใน local storage
        // ไม่ให้ค้างมาปนกับ session ใหม่
        _authState.Token = result.Token;
        _authState.IsRemembered = false;
        await ClearStorageAsync(_localStorage);

        await SaveRefreshTokenAsync(response, _storage); // SSO users need it too, or they can never refresh
        await _storage.SetAsync("authToken", result.Token);
        await _storage.SetAsync("UserID", result.UserID);
        await _storage.SetAsync("fullName", result.FullName ?? string.Empty);
        await _storage.SetAsync("username", result.Username ?? string.Empty);
        await _storage.SetAsync("userRole", result.UserRole ?? string.Empty);
        await _storage.SetAsync("division", result.Division ?? string.Empty);
        await _storage.SetAsync("primaryCompanyID", result.PrimaryCompanyID);
        await _storage.SetAsync("primaryCompanyCode", result.PrimaryCompanyCode ?? string.Empty);
        await _storage.SetAsync("companies", result.Companies);
        await _storage.SetAsync("permission", result.Permission);

        await _authState.InitializeAsync(force: true);
        _broadcast.Publish(result.UserID.ToString()); // other tabs/devices of this user: check now
        return result;
    }
}
