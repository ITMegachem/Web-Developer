using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Helpers;
using Mgt.Lit.Core.Interfaces;
using Mgt.Lit.Core.Services;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration.UserSecrets;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Mgt.Lit.WebApi.Controllers
{
    [ServiceFilter(typeof(ActivityLogFilter))]
    [ApiController]
    [Route("api/member")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepo;
        private readonly IConfiguration _config;
        private readonly AppDbContext _context;
        private readonly IActivityLogService _activityLogService;
        private readonly HttpClient _httpClient;

        public AuthController(IUserRepository userRepo, IConfiguration config, AppDbContext context, IActivityLogService activityLogService, IHttpClientFactory httpClientFactory)
        {
            _userRepo = userRepo;
            _config = config;
            _context = context;
            _activityLogService = activityLogService;
            _httpClient = httpClientFactory.CreateClient();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDto dto)
        {
            Console.WriteLine("API LOGIN: " + dto.Username);

            var user = await _userRepo.LoginAsync(dto.Username, dto.Password);
            if (user == null)
            {
                await WriteLoginLogAsync("PASSWORD", false,
                    username: dto.Username,
                    failReason: "Invalid username or password");
                return Unauthorized("Invalid username or password");
            }

            user.TokenVersion = Guid.NewGuid().ToString();
            await _context.SaveChangesAsync();
            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            cache.Remove($"tv_{user.Username}");
            var companies = await (
                from uc in _context.MsUserCompanies
                join c in _context.MsCompanies on uc.CompanyID equals c.CompanyID
                where uc.UserID == user.UserID
                orderby uc.IsPrimary descending
                select new UserCompanyDto
                {
                    CompanyID = uc.CompanyID,
                    CompanyCode = c.CompanyCode,
                    CompanyName = c.CompanyName,
                    IsPrimary = uc.IsPrimary
                }
            ).ToListAsync();

            var primary = companies.FirstOrDefault(x => x.IsPrimary)
                          ?? companies.FirstOrDefault();

            // ✅ ดึง permission ก่อน GenerateToken
            View_UserPermission? permission = null;
            if (primary?.CompanyID != null)
            {
                permission = await _context.View_UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.UserID == user.UserID &&
                        x.CompanyID == primary.CompanyID);
            }

            // ✅ ส่ง permission เข้า GenerateToken
            var accessToken = JwtHelper.GenerateToken(
                user, _config,
                primaryCompanyId: primary?.CompanyID,
                primaryCompanyCode: primary?.CompanyCode,
                allCompanies: companies,
                permission: permission == null ? null : new UserPermissionDto
                {
                    Page1Access = permission.Page1Access,
                    Page2Access = permission.Page2Access,
                    Page3Access = permission.Page3Access,
                    Page4Access = permission.Page4Access,
                    DataScope = permission.DataScope,
                    CanViewVendor = permission.CanViewVendor,
                    CanViewCost = permission.CanViewCost,
                    Department = permission.Department,
                    CanViewCustomer = permission.CanViewCustomer
                });

            var refreshToken = Guid.NewGuid().ToString();

            var oldTokens = await _context.RefreshTokens
     .Where(r => r.UserID == user.UserID && !r.IsRevoked)
     .ToListAsync();
            foreach (var t in oldTokens) t.IsRevoked = true;

            _context.RefreshTokens.Add(new Core.Entities.RefreshToken
            {
                UserID = (int)user.UserID,
                Token = refreshToken,
                CurrentCompanyID = primary?.CompanyID,
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                IsRevoked = false,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(14)
            });
            await WriteLoginLogAsync("PASSWORD", true,
    username: user.Username,
    email: user.Email,
    userId: (int)user.UserID,
    companyId: primary?.CompanyID);
            return Ok(new
            {
                UserID = user.UserID, // ✅ เพิ่มบรรทัดนี้
                Token = accessToken,
                Username = user.Username,
                FullName = user.FullName,
                UserRole = user.UserRole,
                Division = user.Division,
                PrimaryCompanyID = primary?.CompanyID,
                PrimaryCompanyCode = primary?.CompanyCode,
                Companies = companies,
                Permission = permission == null ? null : new
                {
                    UserID = user.UserID, // 🔥 เพิ่มตรงนี้
                    permission.CompanyID,
                    permission.UserRole,
                    permission.RoleKey,
                    permission.Tier,
                    permission.Page1Access,
                    permission.Page2Access,
                    permission.Page3Access,
                    permission.Page4Access,
                    permission.DataScope,
                    permission.CanViewVendor,
                    permission.CanViewCost,
                    permission.Department,
                    permission.CanViewCustomer
                }
            });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var division = User.FindFirst("Division")?.Value;
            var companyIdStr = User.FindFirst("CompanyID")?.Value;
            int.TryParse(companyIdStr, out var companyId);

            if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(username))
                return Unauthorized();

            IQueryable<MsUser> query = _context.MsUsers;

            switch (role.ToLower())
            {
                case "admin":
                    // 👑 admin → เห็นทุกคน
                    break;

                case "manager":
                    // 👔 manager → เห็น leader ใน division ตัวเอง
                    query = query.Where(u =>
                        u.UserRole == "leader" &&
                        u.Division == division
                    );
                    break;

                case "leader":
                    // 👥 leader → เห็น user ใน division ตัวเอง
                    query = query.Where(u =>
                        u.UserRole == "user" &&
                        u.Division == division
                    );
                    break;

                case "user":
                    // 👤 user → เห็นเฉพาะตัวเอง
                    query = query.Where(u =>
                        u.Username == username
                    );
                    break;

                default:
                    return Forbid();
            }

            var result = await query
                .Select(u => new
                {
                    u.UserID,
                    u.Username,
                    u.FullName,
                    u.UserRole,
                    u.Division,
                    u.IsActive
                })
                .ToListAsync();

            return Ok(result);
        }
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest dto)
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
                return Unauthorized("No username in token");

            var user = await _context.MsUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("User not found");

            if (user.Password != dto.CurrentPassword)
                return BadRequest("Current password is incorrect");

            user.Password = dto.NewPassword;
            await _context.SaveChangesAsync(); // ✅ ไม่ update TokenVersion

            return Ok(new { message = "Password updated" });
        }
        // AuthController.cs — เพิ่ม endpoint ใหม่
        [HttpPost("refresh-explicit")]
        public async Task<IActionResult> RefreshExplicit([FromBody] RefreshExplicitRequest dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RefreshToken))
                return Unauthorized();

            var stored = await _context.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r =>
                    r.Token == dto.RefreshToken &&
                    !r.IsRevoked &&
                    r.ExpiresAt > DateTime.UtcNow);

            if (stored == null)
                return Unauthorized();

            var user = stored.User;
            var currentCompanyId = stored.CurrentCompanyID;

            if (currentCompanyId == null)
            {
                currentCompanyId = await _context.MsUserCompanies
                    .Where(x => x.UserID == user.UserID && x.IsPrimary)
                    .Select(x => (int?)x.CompanyID)
                    .FirstOrDefaultAsync();
            }

            stored.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var userCompanies = await (
                from uc in _context.MsUserCompanies
                join c in _context.MsCompanies on uc.CompanyID equals c.CompanyID
                where uc.UserID == user.UserID
                select new UserCompanyDto
                {
                    CompanyID = uc.CompanyID,
                    CompanyCode = c.CompanyCode,
                    CompanyName = c.CompanyName,
                    IsPrimary = uc.IsPrimary
                }
            ).ToListAsync();

            var primaryCode = userCompanies
                .FirstOrDefault(x => x.CompanyID == currentCompanyId)?.CompanyCode;

            View_UserPermission? permission = null;
            if (currentCompanyId != null)
            {
                permission = await _context.View_UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.UserID == user.UserID &&
                        x.CompanyID == currentCompanyId);
            }

            var newAccessToken = JwtHelper.GenerateToken(
                user, _config, currentCompanyId,
                primaryCompanyCode: primaryCode,
                allCompanies: userCompanies,
                permission: permission == null ? null : new UserPermissionDto
                {
                    Page1Access = permission.Page1Access,
                    Page2Access = permission.Page2Access,
                    Page3Access = permission.Page3Access,
                    Page4Access = permission.Page4Access,
                    DataScope = permission.DataScope,
                    CanViewVendor = permission.CanViewVendor,
                    CanViewCost = permission.CanViewCost,
                    CanViewCustomer = permission.CanViewCustomer
                });

            return Ok(new { token = newAccessToken, currentCompanyId });
        }

        public class RefreshExplicitRequest
        {
            public string RefreshToken { get; set; } = "";
        }
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            if (!Request.Cookies.TryGetValue("refreshToken", out var refreshToken))
                return Unauthorized();

            var stored = await _context.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r =>
                    r.Token == refreshToken &&
                    !r.IsRevoked &&
                    r.ExpiresAt > DateTime.UtcNow);

            if (stored == null)
                return Unauthorized();

            var user = stored.User;
            var currentCompanyId = stored.CurrentCompanyID;

            if (currentCompanyId == null)
            {
                currentCompanyId = await _context.MsUserCompanies
                    .Where(x => x.UserID == user.UserID && x.IsPrimary)
                    .Select(x => (int?)x.CompanyID)
                    .FirstOrDefaultAsync();
            }

            stored.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var userCompanies = await (
                from uc in _context.MsUserCompanies
                join c in _context.MsCompanies on uc.CompanyID equals c.CompanyID
                where uc.UserID == user.UserID
                select new UserCompanyDto
                {
                    CompanyID = uc.CompanyID,
                    CompanyCode = c.CompanyCode,
                    CompanyName = c.CompanyName,
                    IsPrimary = uc.IsPrimary
                }
            ).ToListAsync();

            var primaryCode = userCompanies
                .FirstOrDefault(x => x.CompanyID == currentCompanyId)?.CompanyCode;

            // ✅ ดึง permission
            View_UserPermission? permission = null;
            if (currentCompanyId != null)
            {
                permission = await _context.View_UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.UserID == user.UserID &&
                        x.CompanyID == currentCompanyId);
            }

            // ✅ declare แค่ครั้งเดียว
            var newAccessToken = JwtHelper.GenerateToken(
                user, _config, currentCompanyId,
                primaryCompanyCode: primaryCode,
                allCompanies: userCompanies,
                permission: permission == null ? null : new UserPermissionDto
                {
                    Page1Access = permission.Page1Access,
                    Page2Access = permission.Page2Access,
                    Page3Access = permission.Page3Access,
                    Page4Access = permission.Page4Access,
                    DataScope = permission.DataScope,
                    CanViewVendor = permission.CanViewVendor,
                    CanViewCost = permission.CanViewCost,
                    CanViewCustomer = permission.CanViewCustomer
                }
            );

            return Ok(new { token = newAccessToken, currentCompanyId });
        }
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            if (Request.Cookies.TryGetValue("refreshToken", out var refreshToken))
            {
                var stored = await _context.RefreshTokens
                    .FirstOrDefaultAsync(r => r.Token == refreshToken);

                if (stored != null)
                {
                    stored.IsRevoked = true;
                    await _context.SaveChangesAsync();
                }
            }

            Response.Cookies.Delete("refreshToken");
            return Ok();
        }


        [Authorize]
        [HttpPost("switch-company")]
        public async Task<IActionResult> SwitchCompany([FromBody] SwitchCompanyRequest dto)
        {
            var userIdText = User.FindFirst("UserID")?.Value;
            if (!int.TryParse(userIdText, out var userId))
                return Unauthorized();

            var allowed = await _context.MsUserCompanies
                .AnyAsync(x => x.UserID == userId && x.CompanyID == dto.CompanyID);

            if (!allowed)
                return Forbid();

            var user = await _context.MsUsers.FirstOrDefaultAsync(x => x.UserID == userId);
            if (user == null)
                return Unauthorized();

            if (!Request.Cookies.TryGetValue("refreshToken", out var refreshToken))
                return Unauthorized();

            var storedRefresh = await _context.RefreshTokens
                .FirstOrDefaultAsync(r =>
                    r.Token == refreshToken &&
                    r.UserID == userId &&
                    !r.IsRevoked &&
                    r.ExpiresAt > DateTime.UtcNow);

            if (storedRefresh == null)
                return Unauthorized();

            storedRefresh.CurrentCompanyID = dto.CompanyID;
            storedRefresh.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // ✅ ดึง companies ทั้งหมดของ user
            var allCompanies = await (
                from uc in _context.MsUserCompanies
                join c in _context.MsCompanies on uc.CompanyID equals c.CompanyID
                where uc.UserID == userId
                select new UserCompanyDto
                {
                    CompanyID = uc.CompanyID,
                    CompanyCode = c.CompanyCode,
                    CompanyName = c.CompanyName,
                    IsPrimary = uc.IsPrimary
                }
            ).ToListAsync();

            var switchedCode = allCompanies
                .FirstOrDefault(x => x.CompanyID == dto.CompanyID)?.CompanyCode;

            var newToken = JwtHelper.GenerateToken(
                user, _config, dto.CompanyID,
                primaryCompanyCode: switchedCode,  // ✅ เพิ่ม
                allCompanies: allCompanies         // ✅ เพิ่ม
            );

            var permission = await _context.View_UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserID == userId && x.CompanyID == dto.CompanyID);

            return Ok(new
            {
                token = newToken,
                currentCompanyId = dto.CompanyID,
                Permission = permission == null ? null : new
                {
                    UserID = user.UserID, // 🔥 เพิ่มบรรทัดนี้
                    permission.CompanyID,
                    permission.UserRole,
                    permission.RoleKey,
                    permission.Tier,
                    permission.Page1Access,
                    permission.Page2Access,
                    permission.Page3Access,
                    permission.Page4Access,
                    permission.DataScope,
                    permission.CanViewVendor,
                    permission.CanViewCost,
                    permission.Department,
                    permission.CanViewCustomer
                }
            });
        }
        [HttpGet("microsoft-login-url")]
        public IActionResult GetMicrosoftLoginUrl()
        {
            var tenantId = _config["AzureAd:TenantId"];
            var clientId = _config["AzureAd:ClientId"];
            var redirectUri = _config["AzureAd:RedirectUri"];

            // ✅ เช็ค null ก่อน
            if (string.IsNullOrEmpty(tenantId) ||
                string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(redirectUri))
            {
                return BadRequest(new { message = "AzureAd config is missing" });
            }

            var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize" +
                      $"?client_id={clientId}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope=openid%20profile%20email%20User.Read" +
                      $"&response_mode=query";

            return Ok(new { url });
        }
        [HttpPost("microsoft-callback")]
        public async Task<IActionResult> MicrosoftCallback([FromBody] MicrosoftCallbackDto dto)
        {
            try
            {
                var tokenResponse = await ExchangeCodeForToken(dto.Code);
                var msUser = await GetMicrosoftUserInfo(tokenResponse.AccessToken);

                var email = (msUser.Mail ?? msUser.UserPrincipalName ?? "").Trim();
                if (string.IsNullOrEmpty(email))
                {
                    await WriteLoginLogAsync("SSO", false,
                        failReason: "Microsoft ไม่ส่งอีเมลกลับมา");
                    return Unauthorized(new { message = "Microsoft ไม่ส่งอีเมลกลับมา" });
                }

                Console.WriteLine("MS SSO EMAIL: " + email);

                var user = await _context.MsUsers.FirstOrDefaultAsync(u =>
                    u.Email != null &&
                    u.Email.Trim().ToLower() == email.ToLower() &&
                    u.IsActive);

                if (user == null)
                {
                    await WriteLoginLogAsync("SSO", false,
                        email: email,
                        failReason: "ไม่พบบัญชีในระบบ หรือถูกปิดการใช้งาน");
                    return Unauthorized(new { message = $"ไม่พบบัญชี {email} ในระบบ หรือถูกปิดการใช้งาน" });
                }

                // ✅ ต้องเซ็ตก่อน GenerateToken
                user.TokenVersion = Guid.NewGuid().ToString();

                var companies = await (
                    from uc in _context.MsUserCompanies
                    join c in _context.MsCompanies on uc.CompanyID equals c.CompanyID
                    where uc.UserID == user.UserID
                    orderby uc.IsPrimary descending
                    select new UserCompanyDto
                    {
                        CompanyID = uc.CompanyID,
                        CompanyCode = c.CompanyCode,
                        CompanyName = c.CompanyName,
                        IsPrimary = uc.IsPrimary
                    }
                ).ToListAsync();

                var primary = companies.FirstOrDefault(x => x.IsPrimary)
                              ?? companies.FirstOrDefault();

                if (primary == null)
                {
                    await WriteLoginLogAsync("SSO", false,
                        username: user.Username,
                        email: user.Email,
                        userId: (int)user.UserID,
                        failReason: "บัญชีนี้ยังไม่ได้ผูกกับบริษัทใด");
                    return Unauthorized(new { message = "บัญชีนี้ยังไม่ได้ผูกกับบริษัทใด" });
                }

                View_UserPermission? permission = await _context.View_UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.UserID == user.UserID &&
                        x.CompanyID == primary.CompanyID);

                var accessToken = JwtHelper.GenerateToken(
                    user, _config,
                    primaryCompanyId: primary.CompanyID,
                    primaryCompanyCode: primary.CompanyCode,
                    allCompanies: companies,
                    permission: permission == null ? null : new UserPermissionDto
                    {
                        Page1Access = permission.Page1Access,
                        Page2Access = permission.Page2Access,
                        Page3Access = permission.Page3Access,
                        Page4Access = permission.Page4Access,
                        DataScope = permission.DataScope,
                        CanViewVendor = permission.CanViewVendor,
                        CanViewCost = permission.CanViewCost,
                        Department = permission.Department,
                        CanViewCustomer = permission.CanViewCustomer
                    });

                var refreshToken = Guid.NewGuid().ToString();

                var oldTokens = await _context.RefreshTokens
                    .Where(r => r.UserID == user.UserID && !r.IsRevoked)
                    .ToListAsync();
                foreach (var t in oldTokens)
                    t.IsRevoked = true;

                _context.RefreshTokens.Add(new Core.Entities.RefreshToken
                {
                    UserID = (int)user.UserID,
                    Token = refreshToken,
                    CurrentCompanyID = primary.CompanyID,
                    ExpiresAt = DateTime.UtcNow.AddDays(14),
                    IsRevoked = false,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                // ✅ ล้าง cache หลัง SaveChanges สำเร็จเท่านั้น
                HttpContext.RequestServices.GetRequiredService<IMemoryCache>()
                    .Remove($"tv_{user.Username}");

                Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTime.UtcNow.AddDays(14)
                });

                await WriteLoginLogAsync("SSO", true,
                    username: user.Username,
                    email: user.Email,
                    userId: (int)user.UserID,
                    companyId: primary.CompanyID);

                return Ok(new
                {
                    Token = accessToken,
                    UserID = user.UserID,
                    Username = user.Username,
                    FullName = user.FullName,
                    UserRole = user.UserRole,
                    Division = user.Division,
                    PrimaryCompanyID = primary.CompanyID,
                    PrimaryCompanyCode = primary.CompanyCode,
                    Companies = companies,
                    Permission = permission == null ? null : new
                    {
                        permission.UserID,
                        permission.CompanyID,
                        permission.Department,
                        permission.UserRole,
                        permission.RoleKey,
                        permission.Tier,
                        permission.Page1Access,
                        permission.Page2Access,
                        permission.Page3Access,
                        permission.Page4Access,
                        permission.DataScope,
                        permission.CanViewVendor,
                        permission.CanViewCost,
                        permission.CanViewCustomer
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("MS CALLBACK ERROR: " + ex);
                await WriteLoginLogAsync("SSO", false,
                    failReason: ex.Message.Length > 300 ? ex.Message.Substring(0, 300) : ex.Message);
                return StatusCode(500, new { message = ex.Message });
            }
        }
        // ========== Private Helpers ==========

        private async Task<MicrosoftTokenResponse> ExchangeCodeForToken(string code)
        {
            var tenantId = _config["AzureAd:TenantId"];
            var clientId = _config["AzureAd:ClientId"];
            var clientSecret = _config["AzureAd:ClientSecret"];
            var redirectUri = _config["AzureAd:RedirectUri"];

            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!,
                ["redirect_uri"] = redirectUri!,
                ["scope"] = "openid profile email User.Read"
            });

            var response = await _httpClient.PostAsync(
                $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", body);

            // ✅ อ่าน response เป็น string ก่อน
            var rawJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine("MS Token Response: " + rawJson); // ดู Output window

            var json = System.Text.Json.JsonDocument.Parse(rawJson).RootElement;

            // ✅ เช็คว่ามี error หรือเปล่า
            if (json.TryGetProperty("error", out var error))
            {
                var errorDesc = json.TryGetProperty("error_description", out var desc)
                                ? desc.GetString() : error.GetString();
                throw new Exception($"Microsoft Auth Error: {errorDesc}");
            }

            // ✅ ดึง access_token อย่างปลอดภัย
            if (!json.TryGetProperty("access_token", out var accessTokenProp))
                throw new Exception("access_token not found in response");

            return new MicrosoftTokenResponse
            {
                AccessToken = accessTokenProp.GetString() ?? ""
            };
        }
        private async Task<MicrosoftUserInfo> GetMicrosoftUserInfo(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://graph.microsoft.com/v1.0/me");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            return await response.Content.ReadFromJsonAsync<MicrosoftUserInfo>()
                   ?? new MicrosoftUserInfo();
        }

        // ========== DTOs (ภายใน Controller) ==========

        public class MicrosoftCallbackDto
        {
            public string Code { get; set; } = "";
        }

        public class MicrosoftTokenResponse
        {
            public string AccessToken { get; set; } = "";
        }

        public class MicrosoftUserInfo
        {
            public string? Mail { get; set; }
            public string? DisplayName { get; set; }
            public string? UserPrincipalName { get; set; }
        }
        private async Task WriteLoginLogAsync(
    string method,          // "SSO" หรือ "PASSWORD"
    bool isSuccess,
    string? username = null,
    string? email = null,
    int? userId = null,
    int? companyId = null,
    string? failReason = null)
        {
            var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                     ?? HttpContext.Connection.RemoteIpAddress?.ToString();

            var ua = Request.Headers.UserAgent.ToString();
            if (ua.Length > 500) ua = ua.Substring(0, 500);

            // ✅ เขียนลง Console ก่อนเสมอ — ถ้า DB ล่มยังเห็นร่องรอยได้
            Console.WriteLine($"LOGIN [{method}] {(isSuccess ? "SUCCESS" : "FAILED")} " +
                              $"user={username ?? email ?? "-"} ip={ip} {failReason}");

            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.Log_UserLogin
                (UserID, Username, Email, LoginMethod, IsSuccess,
                 FailReason, IpAddress, UserAgent, CompanyID, LoginAt)
            VALUES
                ({userId}, {username}, {email}, {method}, {isSuccess},
                 {failReason}, {ip}, {ua}, {companyId}, GETDATE())");
            }
            catch (Exception ex)
            {
                // ห้ามให้การเขียน log ทำให้ Login พัง
                Console.WriteLine("LOGIN LOG WRITE FAILED: " + ex.Message);
            }
        }

    }
}
