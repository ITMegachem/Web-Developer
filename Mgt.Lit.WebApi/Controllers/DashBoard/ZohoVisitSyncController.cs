using Mgt.Lit.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mgt.Lit.WebApi.Controllers.DashBoard
{
    // trigger การซิงก์ Visit Report จาก Zoho CRM เข้าตาราง MGT_VisitReport / MGT_VisitItem
    // (ยิงเอง manual ผ่าน endpoint นี้ก่อน — ถ้าต้องการรันอัตโนมัติตามตารางเวลา ค่อยห่อด้วย scheduled job/cron ภายนอกทีหลัง)
    [ApiController]
    [Route("api/dashboard/visitdailyreport")]
    [Authorize(Roles = "admin")]
    public class ZohoVisitSyncController : ControllerBase
    {
        private readonly ZohoVisitSyncService _syncService;

        public ZohoVisitSyncController(ZohoVisitSyncService syncService)
        {
            _syncService = syncService;
        }

        // POST api/dashboard/visitdailyreport/sync
        [HttpPost("sync")]
        public async Task<IActionResult> SyncVisitReports(CancellationToken ct)
        {
            try
            {
                var count = await _syncService.SyncVisitReportsAsync(ct);
                return Ok(new { message = $"Synced {count} visit report(s) from Zoho CRM.", count });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Zoho visit sync failed: {ex.Message}" });
            }
        }
    }
}
