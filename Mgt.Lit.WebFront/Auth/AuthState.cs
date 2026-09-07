using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth;

public class AuthState
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ProtectedLocalStorage _localStorage;
    private bool _isInitialized;

    public string? Token { get; set; }
    public ClaimsPrincipal? User { get; set; }
    public View_UserPermission? Permission { get; set; }
    public string? ExportPassword { get; set; }
    public int? CurrentCompanyID { get; set; }
    public void Clear() { Token = null; User = null; }
    public bool? CanViewCost { get; set; }

    // ★ Remember me — true เมื่อ login ปัจจุบันถูกเก็บใน ProtectedLocalStorage (คงอยู่ข้ามการปิด browser)
    // แทน ProtectedSessionStorage (หายเมื่อปิด browser) — ตั้งตอน login และใช้ตัดสินใจว่า refresh/switch-company
    // รอบถัดไปควรเขียนกลับไปที่ storage ไหน เพื่อไม่ให้ remembered session ถูก "ลดระดับ" เป็น session-only โดยไม่ตั้งใจ
    public bool IsRemembered { get; set; }

    public AuthState(ProtectedSessionStorage sessionStorage, ProtectedLocalStorage localStorage)
    {
        _sessionStorage = sessionStorage;
        _localStorage = localStorage;
    }

    // เช็ค session storage ก่อนเสมอ (login ของแท็บ/รอบนี้) แล้วค่อย fallback ไป local storage (remembered login)
    private async Task<(bool Success, T? Value)> GetAsync<T>(string key)
    {
        var sessionResult = await _sessionStorage.GetAsync<T>(key);
        if (sessionResult.Success) return (true, sessionResult.Value);

        var localResult = await _localStorage.GetAsync<T>(key);
        return (localResult.Success, localResult.Value);
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

        try
        {
            var sessionTokenResult = await _sessionStorage.GetAsync<string>("authToken");
            var tokenSuccess = sessionTokenResult.Success && !string.IsNullOrWhiteSpace(sessionTokenResult.Value);
            var tokenValue = sessionTokenResult.Value;
            IsRemembered = false;

            if (!tokenSuccess)
            {
                var localTokenResult = await _localStorage.GetAsync<string>("authToken");
                if (localTokenResult.Success && !string.IsNullOrWhiteSpace(localTokenResult.Value))
                {
                    tokenSuccess = true;
                    tokenValue = localTokenResult.Value;
                    IsRemembered = true;
                }
            }

            if (!tokenSuccess || string.IsNullOrWhiteSpace(tokenValue))
            {
                _isInitialized = true;
                return;
            }

            Token = tokenValue;

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
    }
}