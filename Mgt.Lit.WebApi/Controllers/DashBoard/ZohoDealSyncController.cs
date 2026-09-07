using Mgt.Lit.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mgt.Lit.WebApi.Controllers.DashBoard
{
    // trigger การซิงก์ Deal จาก Zoho CRM เข้าตาราง MGT_Deal / MGT_DealProduct
    // (ยิงเอง manual ผ่าน endpoint นี้ก่อน — ถ้าต้องการรันอัตโนมัติตามตารางเวลา ค่อยห่อด้วย scheduled job/cron ภายนอกทีหลัง)
    [ApiController]
    [Route("api/dashboard/opportunitywinrate")]
    [Authorize(Roles = "admin")]
    public class ZohoDealSyncController : ControllerBase
    {
        private readonly ZohoDealSyncService _syncService;

        public ZohoDealSyncController(ZohoDealSyncService syncService)
        {
            _syncService = syncService;
        }

        // POST api/dashboard/opportunitywinrate/sync
        // ★ ยิง sync ทุกครั้งจะ capture forecast snapshot ของวันนี้ให้อัตโนมัติด้วย (ดู CaptureForecastSnapshotAsync)
        [HttpPost("sync")]
        public async Task<IActionResult> SyncDeals(CancellationToken ct)
        {
            try
            {
                var count = await _syncService.SyncDealsAsync(ct);
                return Ok(new { message = $"Synced {count} deal(s) from Zoho CRM.", count });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Zoho sync failed: {ex.Message}" });
            }
        }

        // POST api/dashboard/opportunitywinrate/capture-forecast-snapshot
        // เผื่ออยาก capture snapshot วันนี้ใหม่โดยไม่ต้อง sync deal ทั้งชุด (เช่น sync ไปแล้วเมื่อเช้า แต่อยาก snapshot ซ้ำตอนบ่าย)
        [HttpPost("capture-forecast-snapshot")]
        public async Task<IActionResult> CaptureForecastSnapshot(CancellationToken ct)
        {
            try
            {
                var count = await _syncService.CaptureForecastSnapshotAsync(ct);
                return Ok(new { message = $"Captured {count} open deal(s) into today's forecast snapshot.", count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Forecast snapshot capture failed: {ex.Message}" });
            }
        }
    }
}
