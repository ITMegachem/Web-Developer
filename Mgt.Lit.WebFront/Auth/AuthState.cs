using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth;

public class AuthState
{
    private readonly ProtectedLocalStorage _storage;
    private bool _isInitialized;

    public string? Token { get; set; }
    public ClaimsPrincipal? User { get; set; }
    public View_UserPermission? Permission { get; set; }
    public string? ExportPassword { get; set; }
    public int? CurrentCompanyID { get; set; }
    public void Clear() { Token = null; User = null; }
    public bool? CanViewCost { get; set; }
    private readonly IHttpClientFactory _factory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthState(ProtectedLocalStorage storage, IHttpClientFactory factory)
    {
        _storage = storage;
        _factory = factory;
    }
    // fix merge
    // ---------------------------------------------------------------------------------------------
    // Valid-token helpers. The access token now lives in localStorage (shared by every tab), so a tab
    // can open with a token that expired while the browser was closed. Callers ask for a VALID token:
    // expired/expiring → refresh first (one refresh at a time per circuit). After a 401, pass the
    // rejected token to force a refresh unless another call already replaced it.
    // ---------------------------------------------------------------------------------------------
    public async Task<string?> GetValidTokenAsync(string? rejectedToken = null, CancellationToken ct = default)
    {
        if (!_isInitialized) await InitializeAsync();
        if (IsUsable(Token, rejectedToken)) return Token;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (IsUsable(Token, rejectedToken)) return Token;   // refreshed by a concurrent caller

            string? rt = null;
            try { var r = await _storage.GetAsync<string>("refreshToken"); rt = r.Success ? r.Value : null; } catch { }
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

            await _storage.SetAsync("authToken", body.Token);
            if (body.CurrentCompanyId != null) await _storage.SetAsync("currentCompanyID", body.CurrentCompanyId);
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

        try
        {
            var tokenResult = await _storage.GetAsync<string>("authToken");
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Value))
            {
                _isInitialized = true;
                return;
            }

            Token = tokenResult.Value;
            if (force) _sessionEndedRaised = false;

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
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Stored values can't be read (e.g. Data Protection keys changed after a redeploy, or a
            // corrupted entry) → treat as signed out instead of crashing the page.
            Console.WriteLine("[AuthState] Stored session unreadable → signed out: " + ex.Message);
            Token = null; User = null; Permission = null; CurrentCompanyID = null;
            try { await _storage.DeleteAsync("authToken"); await _storage.DeleteAsync("refreshToken"); } catch { }
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