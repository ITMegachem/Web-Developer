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

    public AuthService(HttpClient http, AuthState authState, ProtectedSessionStorage storage)
    {
        _http = http;
        _authState = authState;
        _storage = storage;
    }

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

        await _storage.SetAsync("authToken", result.Token);
        await _storage.SetAsync("fullName", result.FullName ?? string.Empty);
        await _storage.SetAsync("username", result.Username ?? string.Empty);
        await _storage.SetAsync("userRole", result.UserRole ?? string.Empty);
        await _storage.SetAsync("division", result.Division ?? string.Empty);
        await _storage.SetAsync("primaryCompanyID", result.PrimaryCompanyID);
        await _storage.SetAsync("primaryCompanyCode", result.PrimaryCompanyCode ?? string.Empty);
        //await _storage.SetAsync("currentCompanyID", result.CurrentCompanyID ?? result.PrimaryCompanyID);
        await _storage.SetAsync("companies", result.Companies);
        await _storage.SetAsync("permission", result.Permission);

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
                Department = result.Permission.Department,  // ✅ ต้องมีใน LoginResponseDto ด้วย
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
        try
        {
            await _http.PostAsync("api/member/logout", null);
        }
        catch
        {
        }

        _authState.Reset();

        try
        {
            await _storage.DeleteAsync("authToken");
            await _storage.DeleteAsync("fullName");
            await _storage.DeleteAsync("username");
            await _storage.DeleteAsync("userRole");
            await _storage.DeleteAsync("division");
            await _storage.DeleteAsync("primaryCompanyID");
            await _storage.DeleteAsync("primaryCompanyCode");
            await _storage.DeleteAsync("currentCompanyID");
            await _storage.DeleteAsync("companies");
            await _storage.DeleteAsync("permission");
        }
        catch
        {
        }
    }
}