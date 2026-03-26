using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mgt.Lit.WebApi.Controllers   // ← namespace เดียวกับ AuthController
{
    [ApiController]
    [Route("api/download-log")]
    public class DownloadLogController : ControllerBase
    {
        private readonly AppDbContext _db;

        public DownloadLogController(AppDbContext db)
        {
            _db = db;
        }

        [Authorize]          // ✅ ใส่กลับ
        [HttpPost]
        public async Task<IActionResult> Log([FromBody] DownloadLogRequest req)
        {
            // ดึง username จาก Token แทน req.Username เพื่อความปลอดภัย
            var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                           ?? req.Username;

            var user = await _db.View_UserPermissions
                .FirstOrDefaultAsync(u => u.Username == username);

            var log = new DownloadLog
            {
                UserID = user?.UserID.ToString() ?? "",
                Username = username ?? "",
                FullName = user?.FullName,
                CompanyID = user?.CompanyID.ToString(),
                Department = user?.Department,
                Position = user?.Position,
                Division = user?.Division,
                UserRole = user?.UserRole,
                SalesOrganizationCode = user?.SalesOrganizationCode,
                FileName = req.FileName ?? "",
                DownloadedAt = DateTime.UtcNow,
                IpAddress = req.IpAddress,
                UserAgent = req.UserAgent
            };

            _db.DownloadLogs.Add(log);
            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}