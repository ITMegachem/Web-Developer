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
    private readonly ProtectedLocalStorage _storage;
    private readonly SessionBroadcast _broadcast;

    public AuthService(HttpClient http, AuthState authState, ProtectedLocalStorage storage, SessionBroadcast broadcast)
    {
        _http = http;
        _authState = authState;
        _storage = storage;
        _broadcast = broadcast;
    }

    // ✅ LoginAsync อันเดียว — รวมการเก็บ refreshToken ไว้แล้ว
    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto req)
    {
        var response = await _http.PostAsJsonAsync("api/member/login", req);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();

        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            return null;

        _authState.Token = result.Token;

        await SaveRefreshTokenAsync(response);

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

    public async Task<RefreshResponseDto?> RefreshAsync()
    {
        // Blazor Server: this HttpClient runs on the SERVER, so the browser's refreshToken cookie never
        // reaches the API. Send the refresh token we stored at login in the body instead.
        string? rt = null;
        try
        {
            var stored = await _storage.GetAsync<string>("refreshToken");
            rt = stored.Success ? stored.Value : null;
        }
        catch { }
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

    public async Task LogoutAsync()
    {
        // Read what the server needs BEFORE clearing anything.
        string? rt = null;
        try { var r = await _storage.GetAsync<string>("refreshToken"); rt = r.Success ? r.Value : null; } catch { }
        var accessToken = _authState.Token;
        var userId = _authState.User?.FindFirst("UserID")?.Value;

        // 1) Clear this browser FIRST (localStorage is shared by every tab). Each key on its own, so one
        //    failure can't leave the tokens behind — a leftover refreshToken would silently sign the
        //    person straight back in on the next page.
        _authState.Reset();
        foreach (var key in new[] { "authToken", "refreshToken", "UserID", "fullName", "username", "userRole",
                                    "division", "primaryCompanyID", "primaryCompanyCode", "currentCompanyID",
                                    "companies", "permission" })
        {
            try { await _storage.DeleteAsync(key); }
            catch (Exception ex) { Console.WriteLine($"[Logout] could not delete {key}: {ex.Message}"); }
        }
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

        _authState.Token = result.Token;
        await SaveRefreshTokenAsync(response); // SSO users need it too, or they can never refresh
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