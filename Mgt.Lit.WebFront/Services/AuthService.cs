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

    public AuthService(HttpClient http, AuthState authState, ProtectedSessionStorage storage, ProtectedLocalStorage localStorage)
    {
        _http = http;
        _authState = authState;
        _storage = storage;
        _localStorage = localStorage;
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

        // ✅ ดึง refreshToken จาก Set-Cookie header แล้วเก็บใน storage
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var rtCookie = cookies
                .FirstOrDefault(c => c.StartsWith("refreshToken="));
            if (rtCookie != null)
            {
                var rt = rtCookie.Split('=')[1].Split(';')[0];
                await activeStorage.SetAsync("refreshToken", rt);
                Console.WriteLine("[AuthService] refreshToken saved to storage");
            }
        }

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
        return result;
    }

    public async Task<RefreshResponseDto?> RefreshAsync()
    {
        var response = await _http.PostAsync("api/member/refresh", null);

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
        try { await _http.PostAsync("api/member/logout", null); }
        catch { }

        _authState.Reset();

        // ★ ล้างทั้งสอง storage เสมอ ไม่ว่า session นี้จะ "Remember me" ไว้หรือไม่ — กันกรณี logout แล้วยังมี
        //   remembered session เก่าเหลือใน local storage ทำให้ auto-login กลับมาอีกรอบ
        await ClearStorageAsync(_storage);
        await ClearStorageAsync(_localStorage);
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

        _authState.Token = result.Token;
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
        return result;
    }

}