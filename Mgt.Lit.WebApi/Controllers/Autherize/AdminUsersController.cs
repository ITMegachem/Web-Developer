using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.Admin;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mgt.Lit.WebApi.Controllers.Autherize
{
    // User management for admins. Passwords are stored as PasswordHash. TRANSITION: the plaintext
    // Password column is still written too because other systems read it — remove those lines
    // (search "TRANSITION") when the column is retired.
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminUsersController : ControllerBase
    {
        private const int MinPasswordLength = 8;
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public AdminUsersController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // Sign the user out everywhere: new TokenVersion (+ drop the cached one the JWT check uses)
        // and revoke refresh tokens so no tab can refresh its way back in.
        private async Task KickAsync(MsUser user)
        {
            user.TokenVersion = Guid.NewGuid().ToString();
            var tokens = await _context.RefreshTokens.Where(r => r.UserID == user.UserID && !r.IsRevoked).ToListAsync();
            foreach (var t in tokens) t.IsRevoked = true;
            _cache.Remove($"tv_{user.Username}");
        }

        // GET api/admin/users
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var users = await _context.MsUsers.AsNoTracking()
                .OrderBy(u => u.Username)
                .Select(u => new AdminUserListItemDto
                {
                    UserID = u.UserID,
                    Username = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    UserRole = u.UserRole,
                    Division = u.Division,
                    IsActive = u.IsActive,
                    HasPasswordHash = u.PasswordHash != null && u.PasswordHash != ""
                })
                .ToListAsync();

            var links = await (
                from uc in _context.MsUserCompanies.AsNoTracking()
                join c in _context.MsCompanies.AsNoTracking() on uc.CompanyID equals c.CompanyID
                select new { uc.UserID, c.CompanyCode, uc.IsPrimary }
            ).ToListAsync();

            foreach (var u in users)
            {
                var mine = links.Where(l => l.UserID == u.UserID).ToList();
                u.CompanyCodes = mine.Select(l => l.CompanyCode).OrderBy(x => x).ToList();
                u.PrimaryCompanyCode = mine.FirstOrDefault(l => l.IsPrimary)?.CompanyCode;
            }
            return Ok(users);
        }

        // GET api/admin/users/lookups — companies + roles/divisions already used in Ms_User
        [HttpGet("lookups")]
        public async Task<IActionResult> Lookups()
        {
            var companies = await _context.MsCompanies.AsNoTracking()
                .OrderBy(c => c.CompanyID)
                .Select(c => new AdminCompanyItemDto { CompanyID = c.CompanyID, CompanyCode = c.CompanyCode, CompanyName = c.CompanyName })
                .ToListAsync();

            var roles = await _context.MsUsers.AsNoTracking()
                .Where(u => u.UserRole != null && u.UserRole != "")
                .Select(u => u.UserRole!).Distinct().ToListAsync();
            foreach (var r in new[] { "admin", "manager", "leader", "user" })
                if (!roles.Any(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase))) roles.Add(r);

            var divisions = await _context.MsUsers.AsNoTracking()
                .Where(u => u.Division != null && u.Division != "")
                .Select(u => u.Division!).Distinct().ToListAsync();

            return Ok(new AdminUserLookupsDto
            {
                Companies = companies,
                Roles = roles.OrderBy(x => x).ToList(),
                Divisions = divisions.OrderBy(x => x).ToList()
            });
        }

        // POST api/admin/users
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdminCreateUserDto dto)
        {
            var username = (dto.Username ?? "").Trim();
            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

            if (username.Length == 0) return BadRequest("Username is required.");
            if ((dto.Password ?? "").Length < MinPasswordLength)
                return BadRequest($"Password must be at least {MinPasswordLength} characters.");
            if (string.IsNullOrWhiteSpace(dto.UserRole)) return BadRequest("Role is required.");
            if (dto.CompanyIDs == null || dto.CompanyIDs.Count == 0) return BadRequest("Select at least one company.");

            if (await _context.MsUsers.AnyAsync(u => u.Username == username))
                return Conflict("This username already exists.");
            if (email != null && await _context.MsUsers.AnyAsync(u => u.Email == email))
                return Conflict("This email is already used by another user.");

            var companyIds = dto.CompanyIDs.Distinct().ToList();
            var validIds = await _context.MsCompanies.Where(c => companyIds.Contains(c.CompanyID))
                .Select(c => c.CompanyID).ToListAsync();
            if (validIds.Count != companyIds.Count) return BadRequest("Unknown company selected.");
            var primaryId = dto.PrimaryCompanyID is int p && companyIds.Contains(p) ? p : companyIds[0];

            var user = new MsUser
            {
                Username = username,
                FullName = dto.FullName?.Trim(),
                Email = email,
                UserRole = dto.UserRole.Trim(),
                Division = string.IsNullOrWhiteSpace(dto.Division) ? null : dto.Division.Trim(),
                IsActive = dto.IsActive,
                TokenVersion = Guid.NewGuid().ToString(),
                Password = dto.Password              // TRANSITION: other systems still use plaintext
            };
            user.PasswordHash = PasswordService.Hash(user, dto.Password!);

            await using var tx = await _context.Database.BeginTransactionAsync();
            _context.MsUsers.Add(user);
            await _context.SaveChangesAsync();       // get UserID

            foreach (var cid in companyIds)
                _context.MsUserCompanies.Add(new MsUserCompany { UserID = user.UserID, CompanyID = cid, IsPrimary = cid == primaryId });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { user.UserID, user.Username });
        }

        // POST api/admin/users/{id}/reset-password
        [HttpPost("{id:int}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] AdminResetPasswordDto dto)
        {
            if ((dto.NewPassword ?? "").Length < MinPasswordLength)
                return BadRequest($"Password must be at least {MinPasswordLength} characters.");

            var user = await _context.MsUsers.FirstOrDefaultAsync(u => u.UserID == id);
            if (user == null) return NotFound("User not found.");

            user.PasswordHash = PasswordService.Hash(user, dto.NewPassword!);
            user.Password = dto.NewPassword;               // TRANSITION: keep plaintext in sync (old one stops working)
            await KickAsync(user);                         // sign the user out everywhere
            await _context.SaveChangesAsync();
            _cache.Remove($"tv_{user.Username}");
            return Ok(new { message = "Password reset" });
        }

        // POST api/admin/users/{id}/active?value=true|false
        [HttpPost("{id:int}/active")]
        public async Task<IActionResult> SetActive(int id, [FromQuery] bool value)
        {
            var user = await _context.MsUsers.FirstOrDefaultAsync(u => u.UserID == id);
            if (user == null) return NotFound("User not found.");

            var me = User.FindFirst("UserID")?.Value;
            if (!value && me == id.ToString()) return BadRequest("You cannot deactivate your own account.");

            user.IsActive = value;
            if (!value) await KickAsync(user);             // kick a deactivated user
            await _context.SaveChangesAsync();
            _cache.Remove($"tv_{user.Username}");
            return Ok(new { user.UserID, user.IsActive });
        }
    }
}
