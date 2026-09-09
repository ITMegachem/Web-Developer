using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.Core.Services.Dashboard
{
    // Forecast Monthly Report — Forecast (Zoho CRM Deal_Items) x On Hand Stock (SAP), join กันด้วย Material Code
    //
    // ★★★ ข้อจำกัดข้อมูลจริงที่พบตอน verify ★★★
    // - Zoho field "Required_Date" (เดือนที่ต้องการสินค้า) บน Deal_Items ถูกกรอกจริงแค่ ~3% ของบรรทัด (5/172 ตอน verify)
    //   ส่วนใหญ่ Sales ไม่กรอก จึง fallback ไปใช้ "Closing_Date" ของ Deal แม่แทน (มีครบทุก Deal) เมื่อไม่มี Required_Date
    //   — ใช้ตรรกะ "??" เดียวกับที่ ActualClosedDate/ClosingDate fallback ใช้อยู่แล้วในรายงานอื่น
    // - On Hand Qty ดึงจาก SAP API_MATERIAL_STOCK_SRV (A_MatlStkInAcctMod) แบบ live ต่อ Material Code — ยังไม่เคย
    //   ยืนยัน field name ผลลัพธ์จริงกับ tenant นี้ (ต่างจาก Zoho ที่เรียก live ได้เอง) จึงลองหลายชื่อ field ที่เป็นไปได้
    //   ใน GetOnHandQtyAsync — ถ้าไม่ตรงสักชื่อ แถวนั้นจะแสดง OnHandQty = null (ไม่ใช่ 0) เพื่อไม่ให้เข้าใจผิดว่าสต๊อกหมด
    // - Incoming PO ไม่ได้ถูกดึงเข้ารายงานนี้ (ไม่มีตาราง/API ที่ยืนยันได้ตอนสร้างรายงาน) — Shortage จึงคำนวณจาก
    //   Forecast - On Hand เท่านั้น ยังไม่รวม PO ที่กำลังจะเข้า
    public class ForecastMonthlyReportService : IForecastMonthlyReportService
    {
        private readonly AppDbContext _db;
        private readonly SapService _sap;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public ForecastMonthlyReportService(AppDbContext db, SapService sap, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _sap = sap;
            _nameResolver = nameResolver;
        }

        // ★ Sales ทำเครื่องหมายเองว่า deal นี้นับเข้า forecast รอบนี้หรือไม่ (picklist -None-/Yes/No) — รายงานนี้ต้อง
        // กรองเฉพาะ Forecast_Status = "Yes" เสมอ (pattern เดียวกับ SalesForecastAccuracyService.ForecastedDealsQuery)
        // เดิมรายงานนี้ไม่ได้กรองฟิลด์นี้เลย ทำให้รวม Deal_Items ของ deal ที่ยังไม่ถูกทำเครื่องหมายว่านับเข้า forecast เข้ามาด้วย
        private const string ForecastYes = "Yes";

        private sealed record ForecastLineRow(string MaterialCode, string? Product, decimal Quantity, DateTime EffectiveDate);

        public async Task<ForecastMonthlyDto> GetAsync(ForecastMonthlyFilter filter, CancellationToken ct = default)
        {
            var fromMonth = NormalizeMonth(filter.FromMonth ?? DateTime.Now);
            var toMonth = NormalizeMonth(filter.ToMonth ?? fromMonth.AddMonths(5));
            if (toMonth < fromMonth) toMonth = fromMonth;
            var toMonthEnd = toMonth.AddMonths(1).AddDays(-1);

            // ★ นิยาม BU ใหม่ทั้งรายงาน — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedRawNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeRawDealNamesAsync(filter.SalesGroup, ct);

            var dealQuery = _db.MGT_Deal.AsNoTracking().Where(x => x.ForecastStatus == ForecastYes);
            if (lockedRawNames is not null)
                dealQuery = dealQuery.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));
            // ★ SalesEmployeeBP เก็บชื่อบางส่วนจาก Zoho — filter.SalesEmployeeBP เป็น FullName ที่ resolve แล้ว
            // จึงเทียบแบบ substring แทน exact match (ดู pattern เดียวกันใน AverageDaysToCloseService.ApplyScopeFilter)
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                dealQuery = dealQuery.Where(x => x.SalesEmployeeBP != null && filter.SalesEmployeeBP.Contains(x.SalesEmployeeBP));
            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
                dealQuery = dealQuery.Where(x => x.CustomerName == filter.CustomerName);
            if (!string.IsNullOrWhiteSpace(filter.JobDeal))
                dealQuery = dealQuery.Where(x => x.OpportunityName == filter.JobDeal);

            var itemQuery =
                from item in _db.MGT_DealProduct.AsNoTracking()
                join deal in dealQuery on item.DealId equals deal.DealId
                where item.MaterialCode != null && item.MaterialCode != "" && item.Quantity != null
                select new { item, deal };

            if (!string.IsNullOrWhiteSpace(filter.MaterialGroup))
                itemQuery = itemQuery.Where(x => x.item.MaterialGroup == filter.MaterialGroup);
            if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
                itemQuery = itemQuery.Where(x => x.item.MaterialCode == filter.MaterialCode);

            // ★ Effective forecast month = Required_Date ของบรรทัดถ้ามี ไม่งั้น fallback ไป Closing_Date ของ Deal แม่
            var dateFiltered = itemQuery.Where(x =>
                (x.item.RequiredDate ?? x.deal.ClosingDate) >= fromMonth &&
                (x.item.RequiredDate ?? x.deal.ClosingDate) <= toMonthEnd);

            var lines = (await dateFiltered
                .Select(x => new
                {
                    x.item.MaterialCode,
                    x.item.Product,
                    x.item.Quantity,
                    EffectiveDate = x.item.RequiredDate ?? x.deal.ClosingDate
                })
                .ToListAsync(ct))
                .Where(x => x.EffectiveDate.HasValue)
                .Select(x => new ForecastLineRow(x.MaterialCode!, x.Product, x.Quantity!.Value, x.EffectiveDate!.Value))
                .ToList();

            // ── สร้างป้ายกำกับเดือน (คอลัมน์ pivot) ──────────────────────────────────
            var monthCursors = new List<DateTime>();
            var cursor = fromMonth;
            var guard = 0;
            while (cursor <= toMonth && guard < 36)
            {
                monthCursors.Add(cursor);
                cursor = cursor.AddMonths(1);
                guard++;
            }
            var monthLabels = monthCursors.Select(m => m.ToString("yyyy.MM")).ToList();

            // ── Pivot ตาม Material Code ──────────────────────────────────────────────
            var grouped = lines.GroupBy(x => x.MaterialCode).ToList();

            var rows = new List<ForecastMonthlyRowDto>();
            foreach (var g in grouped)
            {
                var monthlyQty = monthCursors
                    .Select(m => g.Where(x => x.EffectiveDate.Year == m.Year && x.EffectiveDate.Month == m.Month).Sum(x => x.Quantity))
                    .ToList();

                rows.Add(new ForecastMonthlyRowDto
                {
                    MaterialCode = g.Key,
                    MaterialDescription = g.Select(x => x.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? g.Key,
                    MonthlyQty = monthlyQty,
                    TotalForecastQty = monthlyQty.Sum()
                });
            }
            rows = rows.OrderBy(r => r.MaterialCode).ToList();

            // ── On Hand Qty จาก SAP — เรียกเป็น batch (~25 material/request แทนทีละตัว) ดู comment ที่ GetOnHandQtyBatchAsync ──
            var stockDict = await GetOnHandQtyBatchAsync(rows.Select(r => r.MaterialCode).ToList(), ct);

            foreach (var row in rows)
            {
                row.OnHandQty = stockDict.TryGetValue(row.MaterialCode, out var qty) ? qty : null;
                row.IsShortage = row.OnHandQty.HasValue && row.TotalForecastQty > row.OnHandQty.Value;
            }

            var totalForecastQty = rows.Sum(r => r.TotalForecastQty);
            var onHandRows = rows.Where(r => r.OnHandQty.HasValue).ToList();
            var totalOnHandQty = onHandRows.Sum(r => r.OnHandQty!.Value);

            var kpi = new ForecastMonthlyKpiDto
            {
                TotalForecastQty = totalForecastQty,
                OnHandQty = totalOnHandQty,
                ShortageQty = Math.Max(0, totalForecastQty - totalOnHandQty),
                MaterialsCount = rows.Count,
                MaterialsWithStockDataCount = onHandRows.Count
            };

            var (availableCustomers, availableMaterialGroups, availableMaterials, availableJobDeals, availableSalesEmployees) =
                await GetDropdownsAsync(filter.SalesGroup, lockedRawNames, ct);

            return new ForecastMonthlyDto
            {
                AsOfDate = DateTime.Now,
                Kpi = kpi,
                MonthLabels = monthLabels,
                Rows = rows,
                AvailableCustomers = availableCustomers,
                AvailableMaterialGroups = availableMaterialGroups,
                AvailableMaterials = availableMaterials,
                AvailableJobDeals = availableJobDeals,
                AvailableSalesEmployees = availableSalesEmployees
            };
        }

        // ★ ลองหลายชื่อ field ที่เป็นไปได้จริงของ API_MATERIAL_STOCK_SRV/A_MatlStkInAcctMod เพราะยังไม่เคยยืนยันกับ
        // tenant นี้โดยตรง — ถ้าไม่พบชื่อไหนตรงเลย คืน null (ไม่ใช่ 0) เพื่อไม่ให้ปนกับ "สต๊อกเป็น 0 จริง"
        private static readonly string[] QuantityFieldCandidates =
        {
            "MatlWrhsStkQtyInMatlBaseUnit", "InventoryStockQtyInBaseUnit", "MatlWrhsStkQtyInCompUOM", "Quantity"
        };

        // ★★★ Performance tuning (2026-09) ★★★ — เดิมยิง 1 SAP request ต่อ Material Code (พบจริงถึง ~70 ครั้งต่อการโหลด
        // 1 ครั้งในข้อมูลจริง) เปลี่ยนเป็นยิงเป็น batch (~25 material/request ผ่าน "or" ใน $filter ของ OData) ลด round trip
        // ไป SAP ลงเหลือไม่กี่ครั้ง รันขนานกันทีละ chunk — พฤติกรรม fail-open ต่อ material ยังเหมือนเดิม (ไม่พบ = N/A ไม่ใช่ 0)
        // ⚠️ ยังไม่เคยยืนยันกับ tenant จริงว่า entity นี้รองรับ "or" หลายเงื่อนไขใน $filter หรือไม่ (OData v2/v4 มาตรฐานรองรับ
        //   แต่ custom entity บางตัวอาจไม่รองรับ) ถ้า batch ทั้งชุดพังจะ fail-open เป็น N/A ทั้ง chunk เหมือนเดิม ไม่ throw ยกรายงาน
        private const int MaterialStockBatchSize = 25;

        private async Task<Dictionary<string, decimal>> GetOnHandQtyBatchAsync(List<string> materialCodes, CancellationToken ct)
        {
            var distinctCodes = materialCodes.Distinct().ToList();
            if (distinctCodes.Count == 0) return new Dictionary<string, decimal>();

            var chunks = distinctCodes
                .Select((code, idx) => (code, idx))
                .GroupBy(x => x.idx / MaterialStockBatchSize)
                .Select(g => g.Select(x => x.code).ToList())
                .ToList();

            var chunkResults = await Task.WhenAll(chunks.Select(chunk => GetOnHandQtyForChunkAsync(chunk, ct)));

            var merged = new Dictionary<string, decimal>();
            foreach (var chunkDict in chunkResults)
                foreach (var kv in chunkDict)
                    merged[kv.Key] = merged.TryGetValue(kv.Key, out var existing) ? existing + kv.Value : kv.Value;

            return merged;
        }

        private async Task<Dictionary<string, decimal>> GetOnHandQtyForChunkAsync(List<string> materialCodes, CancellationToken ct)
        {
            var result = new Dictionary<string, decimal>();
            try
            {
                var json = await _sap.GetMaterialStockBatchAsync(materialCodes);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // OData v2 มาตรฐาน: { "d": { "results": [ {...} ] } }
                var results = root.TryGetProperty("d", out var d) && d.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Array
                    ? r
                    : (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v : default);

                if (results.ValueKind != JsonValueKind.Array) return result;

                foreach (var item in results.EnumerateArray())
                {
                    var material = item.TryGetProperty("Material", out var matProp) && matProp.ValueKind == JsonValueKind.String
                        ? matProp.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(material)) continue;

                    foreach (var field in QuantityFieldCandidates)
                    {
                        if (!item.TryGetProperty(field, out var qv)) continue;
                        decimal? qty = qv.ValueKind switch
                        {
                            JsonValueKind.String when decimal.TryParse(qv.GetString(), out var parsed) => parsed,
                            JsonValueKind.Number => qv.GetDecimal(),
                            _ => null
                        };
                        if (qty.HasValue)
                        {
                            result[material] = result.TryGetValue(material, out var existing) ? existing + qty.Value : qty.Value;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // best-effort — chunk ที่ SAP ไม่ตอบไม่ควรทำให้รายงานทั้งชุดล้ม (material ใน chunk นี้จะได้ N/A แทน)
            }
            return result;
        }

        private async Task<(List<string> Customers, List<string> MaterialGroups, List<string> Materials, List<string> JobDeals, List<string> SalesEmployees)>
            GetDropdownsAsync(string? salesGroup, List<string>? lockedRawNames, CancellationToken ct)
        {
            var dealQuery = _db.MGT_Deal.AsNoTracking().Where(x => x.ForecastStatus == ForecastYes);
            if (lockedRawNames is not null)
                dealQuery = dealQuery.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));

            var customers = await dealQuery.Select(x => x.CustomerName)
                .Where(v => v != null && v != "").Select(v => v!).Distinct().OrderBy(v => v).ToListAsync(ct);

            var jobDeals = await dealQuery.Select(x => x.OpportunityName)
                .Where(v => v != null && v != "").Select(v => v!).Distinct().OrderBy(v => v).ToListAsync(ct);

            var salesEmployeesRaw = await dealQuery.Select(x => x.SalesEmployeeBP)
                .Where(v => v != null && v != "").Select(v => v!).Distinct().ToListAsync(ct);

            // ★ SalesEmployeeBP เก็บชื่อบางส่วนจาก Zoho — map เป็น Ms_User.FullName ก่อนแสดงผล (pattern เดียวกับรายงานอื่น)
            var salesEmployeeNameMap = await _nameResolver.BuildNameMapAsync(salesEmployeesRaw, ct);
            var salesEmployeesMapped = salesEmployeesRaw
                .Select(raw => salesEmployeeNameMap.TryGetValue(raw, out var mapped) ? mapped : raw)
                .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "Department", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            // ★ เมื่อ BU ถูกล็อก (salesGroup มีค่า, กรณีนี้คือ Leader) dropdown "Salesperson" แสดงเฉพาะพนักงานที่ Home
            // Division (Ms_User.Division) ตรงกับ BU นี้เท่านั้น
            var salesEmployees = await _nameResolver.FilterToDivisionAsync(salesEmployeesMapped, salesGroup, ct);

            var itemQuery = from item in _db.MGT_DealProduct.AsNoTracking()
                            join deal in dealQuery on item.DealId equals deal.DealId
                            select item;

            var materialGroups = await itemQuery.Select(x => x.MaterialGroup)
                .Where(v => v != null && v != "").Select(v => v!).Distinct().OrderBy(v => v).ToListAsync(ct);

            var materials = await itemQuery.Where(x => x.MaterialCode != null && x.MaterialCode != "")
                .Select(x => new { x.MaterialCode, x.Product })
                .Distinct()
                .ToListAsync(ct);

            var materialLabels = materials
                .GroupBy(x => x.MaterialCode)
                .Select(g => $"{g.Key} - {g.Select(x => x.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? g.Key}")
                .OrderBy(v => v)
                .ToList();

            return (customers, materialGroups, materialLabels, jobDeals, salesEmployees);
        }

        private static DateTime NormalizeMonth(DateTime value) => new(value.Year, value.Month, 1);
    }
}
