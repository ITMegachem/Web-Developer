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

        public AuthController(IUserRepository userRepo, IConfiguration config, AppDbContext context, IActivityLogService activityLogService)
        {
            _userRepo = userRepo;
            _config = config;
            _context = context;
            _activityLogService = activityLogService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDto dto)
        {
            Console.WriteLine("API LOGIN: " + dto.Username);

            var user = await _userRepo.LoginAsync(dto.Username, dto.Password);
            if (user == null)
                return Unauthorized("Invalid username or password");

            user.TokenVersion = Guid.NewGuid().ToString();
            await _context.SaveChangesAsync();

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

            var accessToken = JwtHelper.GenerateToken(
     user,
     _config,
     primaryCompanyId: primary?.CompanyID,
     primaryCompanyCode: primary?.CompanyCode,  // ✅ เพิ่ม
     allCompanies: companies                    // ✅ เพิ่ม
 );

            var refreshToken = Guid.NewGuid().ToString();

            var oldTokens = _context.RefreshTokens
                .Where(r => r.UserID == user.UserID && !r.IsRevoked);

            foreach (var t in oldTokens)
                t.IsRevoked = true;

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

            View_UserPermission? permission = null;

            if (primary?.CompanyID != null)
            {
                permission = await _context.View_UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.UserID == user.UserID &&
                        x.CompanyID == primary.CompanyID);
            }

            return Ok(new
            {
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
                    permission.CompanyID,
                    permission.UserRole,
                    permission.RoleKey,
                    permission.Tier,
                    permission.Page1Access,
                    permission.Page2Access,
                    permission.Page3Access,
                    permission.DataScope,
                    permission.CanViewVendor,
                    permission.CanViewCost
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
                return Unauthorized();

            var user = await _context.MsUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("User not found");

            // ✅ เทียบแบบตรง ๆ (ตาม DB ที่เก็บ Password เป็น text)
            if (user.Password != dto.CurrentPassword)
                return BadRequest("Current password is incorrect");

            user.Password = dto.NewPassword;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password updated" });
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

            // ✅ ดึง companies ทั้งหมดของ user
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

            var newAccessToken = JwtHelper.GenerateToken(
                user, _config, currentCompanyId,
                primaryCompanyCode: primaryCode,   // ✅ เพิ่ม
                allCompanies: userCompanies        // ✅ เพิ่ม
            );

            return Ok(new
            {
                token = newAccessToken,
                currentCompanyId
            });
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
            var userIdText = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
                permission = permission == null ? null : new
                {
                    permission.CompanyID,
                    permission.UserRole,
                    permission.RoleKey,
                    permission.Tier,
                    permission.Page1Access,
                    permission.Page2Access,
                    permission.Page3Access,
                    permission.DataScope,
                    permission.CanViewVendor,
                    permission.CanViewCost
                }
            });
        }
    }
}
