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
        private static readonly string[] AllowedScopes = { "OWN", "DIVISION", "COMPANY", "ALL_COMPANIES" };

        // Map legacy spellings (CROSS_COMPANY, ALL_COMPANY, ALL, lower case) to the 4 values the UI offers.
        private static string? CanonicalScope(string? raw)
        {
            var v = (raw ?? "").Trim().ToUpperInvariant();
            return v switch
            {
                "" => "OWN",
                "CROSS_COMPANY" or "ALL_COMPANY" or "ALL" => "ALL_COMPANIES",
                _ => AllowedScopes.Contains(v) ? v : null
            };
        }
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
                    Tier = u.Tier,
                    Department = u.Department,
                    Position = u.Position,
                    IsActive = u.IsActive,
                    HasPasswordHash = u.PasswordHash != null && u.PasswordHash != ""
                })
                .ToListAsync();

            var links = await (
                from uc in _context.MsUserCompanies.AsNoTracking()
                join c in _context.MsCompanies.AsNoTracking() on uc.CompanyID equals c.CompanyID
                select new { uc.UserID, uc.CompanyID, c.CompanyCode, uc.IsPrimary }
            ).ToListAsync();

            foreach (var u in users)
            {
                var mine = links.Where(l => l.UserID == u.UserID).ToList();
                u.CompanyCodes = mine.Select(l => l.CompanyCode).OrderBy(x => x).ToList();
                u.PrimaryCompanyCode = mine.FirstOrDefault(l => l.IsPrimary)?.CompanyCode;
                u.CompanyIDs = mine.Select(l => l.CompanyID).ToList();
                u.PrimaryCompanyID = mine.FirstOrDefault(l => l.IsPrimary)?.CompanyID;
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

            // Canonical spellings first (they match View_UserPermission / Ms_RolePermission),
            // then any other role already in Ms_User — de-duplicated ignoring case ("User" = "user").
            var dbRoles = await _context.MsUsers.AsNoTracking()
                .Where(u => u.UserRole != null && u.UserRole != "")
                .Select(u => u.UserRole!).Distinct().ToListAsync();
            var roles = CanonicalRoles.ToList();
            foreach (var r in dbRoles.Select(x => x.Trim()))
                if (!roles.Any(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase))) roles.Add(r);

            var divisions = await _context.MsUsers.AsNoTracking()
                .Where(u => u.Division != null && u.Division != "")
                .Select(u => u.Division!).Distinct().ToListAsync();

            var tiers = await _context.MsUsers.AsNoTracking()
                .Where(u => u.Tier != null && u.Tier != "")
                .Select(u => u.Tier!).Distinct().ToListAsync();
            foreach (var t in new[] { "Tier1", "Tier2", "Tier3", "Tier4" })
                if (!tiers.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) tiers.Add(t);

            var departments = await _context.MsUsers.AsNoTracking()
                .Where(u => u.Department != null && u.Department != "")
                .Select(u => u.Department!).Distinct().ToListAsync();
            var positions = await _context.MsUsers.AsNoTracking()
                .Where(u => u.Position != null && u.Position != "")
                .Select(u => u.Position!).Distinct().ToListAsync();

            return Ok(new AdminUserLookupsDto
            {
                Companies = companies,
                Roles = roles,
                Divisions = divisions.OrderBy(x => x).ToList(),
                Tiers = tiers.OrderBy(x => x).ToList(),
                Departments = departments.OrderBy(x => x).ToList(),
                Positions = positions.OrderBy(x => x).ToList()
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
                UserRole = CanonicalRole(dto.UserRole),
                Division = string.IsNullOrWhiteSpace(dto.Division) ? null : dto.Division.Trim(),
                Tier = Clean(dto.Tier),
                Department = Clean(dto.Department),
                Position = Clean(dto.Position),
                IsActive = dto.IsActive,
                TokenVersion = Guid.NewGuid().ToString(),
                Password = dto.Password              // TRANSITION: other systems still use plaintext
            };
            user.PasswordHash = PasswordService.Hash(user, dto.Password!);

            // EnableRetryOnFailure is on → a manual transaction must run inside the execution strategy.
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                _context.MsUsers.Add(user);
                await _context.SaveChangesAsync();       // get UserID

                foreach (var cid in companyIds)
                    _context.MsUserCompanies.Add(new MsUserCompany { UserID = user.UserID, CompanyID = cid, IsPrimary = cid == primaryId });
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            });

            return Ok(new { user.UserID, user.Username });
        }

        // GET api/admin/users/{id}/permissions — the user's Ms_UserPermissionOverride row as a 0/1-item list
        [HttpGet("{id:int}/permissions")]
        public async Task<IActionResult> Permissions(int id)
        {
            var rows = await _context.MsUserPermissionOverrides.AsNoTracking()
                .Where(o => o.UserID == id)
                .Select(o => new AdminPermissionOverrideDto
                {
                    CompanyID = o.CompanyID,
                    DataScope = o.DataScope ?? "",
                    Page1Access = o.Page1Access ?? false,
                    Page2Access = o.Page2Access ?? false,
                    Page3Access = o.Page3Access ?? false,
                    Page4Access = o.Page4Access != 0,
                    CanViewVendor = o.CanViewVendor ?? false,
                    CanViewCost = o.CanViewCost ?? false,
                    CanViewCustomer = o.CanViewCustomer != 0
                })
                .ToListAsync();
            foreach (var r in rows) r.DataScope = CanonicalScope(r.DataScope) ?? r.DataScope.Trim().ToUpperInvariant();
            return Ok(rows);
        }

        // PUT api/admin/users/{id} — edit profile, role, division, active flag and companies (Ms_UserCompany).
        // Username and password are not changed here (password → reset-password).
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateUserDto dto)
        {
            var user = await _context.MsUsers.FirstOrDefaultAsync(u => u.UserID == id);
            if (user == null) return NotFound("User not found.");

            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            var role = CanonicalRole(dto.UserRole ?? "");
            if (role.Length == 0) return BadRequest("Role is required.");
            if (dto.CompanyIDs == null || dto.CompanyIDs.Count == 0) return BadRequest("Select at least one company.");
            if (email != null && await _context.MsUsers.AnyAsync(u => u.Email == email && u.UserID != id))
                return Conflict("This email is already used by another user.");

            var isMe = User.FindFirst("UserID")?.Value == id.ToString();
            if (isMe && !dto.IsActive) return BadRequest("You cannot deactivate your own account.");
            if (isMe && !string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                return BadRequest("You cannot remove the admin role from your own account.");

            var companyIds = dto.CompanyIDs.Distinct().ToList();
            var validIds = await _context.MsCompanies.Where(c => companyIds.Contains(c.CompanyID))
                .Select(c => c.CompanyID).ToListAsync();
            if (validIds.Count != companyIds.Count) return BadRequest("Unknown company selected.");
            var primaryId = dto.PrimaryCompanyID is int p && companyIds.Contains(p) ? p : companyIds[0];

            if (dto.UpdateOverride && dto.Override != null)
            {
                var o = dto.Override;
                var scope = CanonicalScope(o.DataScope);
                if (scope == null) return BadRequest($"Invalid DataScope \"{o.DataScope}\".");
                o.DataScope = scope;
            }

            var division = string.IsNullOrWhiteSpace(dto.Division) ? null : dto.Division.Trim();
            var tier = Clean(dto.Tier);
            var department = Clean(dto.Department);
            var position = Clean(dto.Position);
            var existing = await _context.MsUserCompanies.Where(uc => uc.UserID == id).ToListAsync();

            // Anything that changes what the token/permissions say → sign the user out so they log in fresh.
            var oldIds = existing.Select(e => e.CompanyID).OrderBy(x => x).ToList();
            var accessChanged =
                !string.Equals(user.UserRole, role, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(user.Division ?? "", division ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(user.Tier ?? "", tier ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(user.Department ?? "", department ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(user.Position ?? "", position ?? "", StringComparison.OrdinalIgnoreCase)
                || (user.IsActive && !dto.IsActive)
                || !oldIds.SequenceEqual(companyIds.OrderBy(x => x));

            user.FullName = string.IsNullOrWhiteSpace(dto.FullName) ? null : dto.FullName.Trim();
            user.Email = email;
            user.UserRole = role;
            user.Division = division;
            user.Tier = tier;
            user.Department = department;
            user.Position = position;
            user.IsActive = dto.IsActive;

            // All changes below go out in ONE SaveChanges, which EF wraps in its own transaction.
            // (No manual BeginTransaction: EnableRetryOnFailure would reject it.)

            // Sync Ms_UserCompany: remove unticked, add new, set IsPrimary on exactly one row.
            foreach (var row in existing.Where(e => !companyIds.Contains(e.CompanyID)))
                _context.MsUserCompanies.Remove(row);
            foreach (var cid in companyIds.Where(c => existing.All(e => e.CompanyID != c)))
                _context.MsUserCompanies.Add(new MsUserCompany { UserID = id, CompanyID = cid, IsPrimary = cid == primaryId });
            foreach (var row in existing.Where(e => companyIds.Contains(e.CompanyID)))
                row.IsPrimary = row.CompanyID == primaryId;

            // Ms_UserPermissionOverride (PK = UserID → one row per user).
            // CompanyID always mirrors the user's primary company in Ms_UserCompany.
            var ov = await _context.MsUserPermissionOverrides.FirstOrDefaultAsync(o => o.UserID == id);
            if (!dto.UpdateOverride)
            {
                if (ov != null && ov.CompanyID != primaryId) { ov.CompanyID = primaryId; accessChanged = true; }
            }
            else
            {
                if (dto.Override == null)
                {
                    if (ov != null) { _context.MsUserPermissionOverrides.Remove(ov); accessChanged = true; }
                }
                else
                {
                    var n = dto.Override;
                    if (ov == null)
                    {
                        ov = new MsUserPermissionOverride { UserID = id };
                        _context.MsUserPermissionOverrides.Add(ov);
                    }
                    var before = Sig(ov);
                    ov.CompanyID = primaryId;
                    ov.DataScope = n.DataScope;
                    ov.Page1Access = n.Page1Access;
                    ov.Page2Access = n.Page2Access;
                    ov.Page3Access = n.Page3Access;
                    ov.Page4Access = n.Page4Access ? 1 : 0;
                    ov.CanViewVendor = n.CanViewVendor;
                    ov.CanViewCost = n.CanViewCost;
                    ov.CanViewCustomer = n.CanViewCustomer ? 1 : 0;
                    if (before != Sig(ov)) accessChanged = true;
                }
            }

            if (accessChanged && !isMe) await KickAsync(user);

            await _context.SaveChangesAsync();
            _cache.Remove($"tv_{user.Username}");

            return Ok(new { user.UserID, user.Username, signedOut = accessChanged && !isMe });
        }

        // Spelling used by View_UserPermission's RoleKey CASE and Ms_RolePermission.
        private static readonly string[] CanonicalRoles = { "user", "Leader", "Manager", "Admin" };
        private static string CanonicalRole(string role) =>
            CanonicalRoles.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase)) ?? role.Trim();

        private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        private static string Sig(MsUserPermissionOverride o) =>
            $"{o.CompanyID}|{o.DataScope}|{o.Page1Access}|{o.Page2Access}|{o.Page3Access}|{o.Page4Access}|{o.CanViewVendor}|{o.CanViewCost}|{o.CanViewCustomer}";

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
