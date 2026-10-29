using Mgt.Lit.Core.Services;

namespace Mgt.Lit.WebApi.BackgroundServices
{
    // ซิงก์ Deal + Visit Report จาก Zoho CRM อัตโนมัติ — ดึงเฉพาะข้อมูลที่ใหม่/แก้ไขตั้งแต่รอบที่สำเร็จล่าสุด (incremental)
    // รอบแรกหลังแอปสตาร์ทเป็น full sync และทำ full sync ซ้ำทุก FullSyncEveryHours
    // เพราะการเปลี่ยนแปลงบน Account (Owner/Industry/Sales Group) ไม่ทำให้ Modified_Time ของ Deal เปลี่ยน incremental จึงมองไม่เห็น
    // ตั้งค่าได้ใน appsettings: "ZohoSync": { "Enabled": true, "IntervalHours": 1, "FullSyncEveryHours": 24, "StartupDelaySeconds": 30 }
    public class ZohoAutoSyncService : BackgroundService
    {
        // เผื่อเวลาเหลื่อมระหว่างนาฬิกาเครื่องนี้กับ Zoho และ record ที่ถูกแก้ระหว่างรอบ sync กำลังรัน (upsert ซ้ำไม่เสียหาย)
        private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ZohoAutoSyncService> _logger;

        private DateTimeOffset? _dealWatermark;
        private DateTimeOffset? _visitWatermark;
        private DateTimeOffset? _lastFullSync;

        public ZohoAutoSyncService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ZohoAutoSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("ZohoSync:Enabled", true))
            {
                _logger.LogInformation("Zoho auto-sync is disabled (ZohoSync:Enabled=false).");
                return;
            }

            var intervalHours = Math.Max(1, _config.GetValue("ZohoSync:IntervalHours", 1));
            var startupDelay = TimeSpan.FromSeconds(Math.Max(0, _config.GetValue("ZohoSync:StartupDelaySeconds", 30)));

            try
            {
                await Task.Delay(startupDelay, stoppingToken);

                using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
                do
                {
                    await RunOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // app กำลัง shutdown
            }
        }

        // Deal กับ Visit แยก try/catch กัน — ตัวใดตัวหนึ่งพังต้องไม่ทำให้อีกตัวไม่ได้ sync
        // watermark ขยับเฉพาะเมื่อ sync ตัวนั้นสำเร็จ — ถ้าล้มรอบหน้าจะดึงช่วงเดิมซ้ำโดยอัตโนมัติ
        private async Task RunOnceAsync(CancellationToken ct)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var fullSyncHours = Math.Max(1, _config.GetValue("ZohoSync:FullSyncEveryHours", 24));
            var isFull = _lastFullSync is null
                || _dealWatermark is null
                || _visitWatermark is null
                || startedAt - _lastFullSync.Value >= TimeSpan.FromHours(fullSyncHours);

            var dealSince = isFull ? null : _dealWatermark;
            var visitSince = isFull ? null : _visitWatermark;
            var mode = isFull ? "full" : "incremental";

            using var scope = _scopeFactory.CreateScope();
            var dealOk = false;
            var visitOk = false;

            try
            {
                var deals = scope.ServiceProvider.GetRequiredService<ZohoDealSyncService>();
                var count = await deals.SyncDealsAsync(dealSince, ct);
                _dealWatermark = startedAt - Overlap;
                dealOk = true;
                _logger.LogInformation("Zoho auto-sync ({Mode}): synced {Count} deal(s).", mode, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Zoho auto-sync ({Mode}): deal sync failed.", mode);
            }

            try
            {
                var visits = scope.ServiceProvider.GetRequiredService<ZohoVisitSyncService>();
                var count = await visits.SyncVisitReportsAsync(visitSince, ct);
                _visitWatermark = startedAt - Overlap;
                visitOk = true;
                _logger.LogInformation("Zoho auto-sync ({Mode}): synced {Count} visit report(s).", mode, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Zoho auto-sync ({Mode}): visit sync failed.", mode);
            }

            // นับเป็น full sync สำเร็จก็ต่อเมื่อทั้งสองตัวผ่าน ไม่งั้นรอบหน้าลอง full ใหม่
            if (isFull && dealOk && visitOk) _lastFullSync = startedAt;
        }
    }
}
