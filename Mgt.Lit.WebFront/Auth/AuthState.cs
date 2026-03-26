using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth;

public class AuthState
{
    private readonly ProtectedSessionStorage _storage;
    private bool _isInitialized;

    public string? Token { get; set; }
    public ClaimsPrincipal? User { get; set; }
    public View_UserPermission? Permission { get; set; }
    public int? CurrentCompanyID { get; set; }
    public void Clear() { Token = null; User = null; }
    public AuthState(ProtectedSessionStorage storage)
    {
        _storage = storage;
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
            var tokenResult = await _storage.GetAsync<string>("authToken");
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Value))
            {
                _isInitialized = true;
                return;
            }

            Token = tokenResult.Value;

            var fullNameResult = await _storage.GetAsync<string>("fullName");
            var fullName = fullNameResult.Success ? (fullNameResult.Value ?? "") : "";

            var permissionResult = await _storage.GetAsync<View_UserPermission>("permission");
            Permission = permissionResult.Success ? permissionResult.Value : null;

            var companyResult = await _storage.GetAsync<int?>("currentCompanyID");
            CurrentCompanyID = companyResult.Success ? companyResult.Value : null;

            if (CurrentCompanyID == null)
            {
                var primaryCompanyResult = await _storage.GetAsync<int?>("primaryCompanyID");
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