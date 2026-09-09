using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Data;

namespace Mgt.Lit.Core.Services.Dashboard
{
    // Sales Forecast Accuracy Report — อ้างอิงจาก Zoho CRM เท่านั้น ไม่เกี่ยวกับ SAP/MGT_Sale
    // กรองเฉพาะ Deal ที่ Forecast_Status = "Yes" (Sales ทำเครื่องหมายเองว่านับเข้า forecast รอบนี้)
    // แล้ววิเคราะห์ว่าสุดท้าย Won / Lost / ยังเปิดอยู่ (Pending) เท่าไร ตาม ForecastCategory ที่ sync ไว้แล้ว
    public class SalesForecastAccuracyService : ISalesForecastAccuracyService
    {
        private const string ForecastYes = "Yes";
        private const string WonCategory = "Closed";
        private const string LostCategory = "Omitted";

        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public SalesForecastAccuracyService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private sealed record DealRow(
            string DealId, string? OpportunityName, string? CustomerName, string? SalesEmployeeBP, string? IndustryName,
            decimal? DealAmount, DateTime? ClosingDate, string? Stage, string? ForecastCategory);

        public async Task<SalesForecastAccuracyDto> GetAsync(SalesForecastAccuracyFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งรายงาน — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedRawNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeRawDealNamesAsync(filter.SalesGroup, ct);

            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var scopeQuery = ForecastedDealsQuery(filter, lockedRawNames);

            if (filter.DateFrom.HasValue) scopeQuery = scopeQuery.Where(x => x.ClosingDate >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) scopeQuery = scopeQuery.Where(x => x.ClosingDate <= filter.DateTo.Value);

            var mainRows = (await scopeQuery
                .Select(x => new
                {
                    x.DealId, x.OpportunityName, x.CustomerName, x.SalesEmployeeBP, x.IndustryName,
                    x.DealAmount, x.ClosingDate, x.Stage, x.ForecastCategory
                })
                .ToListAsync(ct))
                .Select(x => new DealRow(x.DealId, x.OpportunityName, x.CustomerName, x.SalesEmployeeBP, x.IndustryName,
                    x.DealAmount, x.ClosingDate, x.Stage, x.ForecastCategory))
                .ToList();

            var won = mainRows.Where(x => x.ForecastCategory == WonCategory).ToList();
            var lost = mainRows.Where(x => x.ForecastCategory == LostCategory).ToList();
            var pending = mainRows.Where(x => x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory).ToList();

            var wonValue = won.Sum(x => x.DealAmount ?? 0);
            var lostValue = lost.Sum(x => x.DealAmount ?? 0);
            var pendingValue = pending.Sum(x => x.DealAmount ?? 0);
            var totalValue = mainRows.Sum(x => x.DealAmount ?? 0);
            var decidedCount = won.Count + lost.Count;

            var kpi = new ForecastAccuracyKpiDto
            {
                TotalForecastedDeals = mainRows.Count,
                TotalForecastValue = totalValue,
                WonDeals = won.Count,
                WonValue = wonValue,
                LostDeals = lost.Count,
                LostValue = lostValue,
                PendingDeals = pending.Count,
                PendingValue = pendingValue,
                DecidedDeals = decidedCount,
                ForecastWinRatePercent = decidedCount == 0 ? null : Math.Round(won.Count * 100m / decidedCount, 1),
                ForecastValueRealizationPercent = (wonValue + lostValue) == 0 ? null : Math.Round(wonValue * 100m / (wonValue + lostValue), 1),
                PendingRatePercent = mainRows.Count == 0 ? 0 : Math.Round(pending.Count * 100m / mainRows.Count, 1)
            };

            // ── Trend รายเดือน ตาม ClosingDate ──────────────────────────────────────────
            var trend = mainRows
                .Where(x => x.ClosingDate.HasValue)
                .GroupBy(x => new DateTime(x.ClosingDate!.Value.Year, x.ClosingDate.Value.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => BuildTrendPoint(g.Key, g.ToList()))
                .ToList();

            // ── Breakdown: Salesperson / Customer / Industry ────────────────────────────
            var bySalesperson = BuildGroupBreakdown(mainRows, x => x.SalesEmployeeBP);
            var byCustomer = BuildGroupBreakdown(mainRows, x => x.CustomerName);
            var byIndustry = BuildGroupBreakdown(mainRows, x => x.IndustryName);

            // ── Breakdown: Product (join ตาราง MGT_DealProduct — 1 Deal นับได้หลาย Product) ──
            var byProduct = await GetByProductAsync(filter, lockedRawNames, ct);

            // ── รายการ Deal ที่ forecast ไว้ (เรียงตามวันที่คาดว่าจะปิด ใกล้สุดก่อน) ──────
            var recentDealIds = mainRows.Select(x => x.DealId).ToList();
            var productsByDeal = recentDealIds.Count == 0
                ? new Dictionary<string, string>()
                : (await _db.MGT_DealProduct.AsNoTracking()
                    .Where(p => recentDealIds.Contains(p.DealId))
                    .Select(p => new { p.DealId, p.Product })
                    .ToListAsync(ct))
                    .Where(p => !string.IsNullOrWhiteSpace(p.Product))
                    .GroupBy(p => p.DealId)
                    .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Product).Distinct()));

            var forecastedDeals = mainRows
                .OrderBy(x => x.ClosingDate)
                .Select(x => new ForecastedDealRowDto
                {
                    DealId = x.DealId,
                    OpportunityName = x.OpportunityName ?? "",
                    CustomerName = x.CustomerName ?? "",
                    SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                    Product = productsByDeal.TryGetValue(x.DealId, out var p) ? p : "",
                    IndustryName = x.IndustryName ?? "",
                    DealAmount = x.DealAmount ?? 0,
                    ClosingDate = x.ClosingDate,
                    Stage = x.Stage ?? "",
                    Outcome = GetOutcome(x.ForecastCategory)
                })
                .ToList();

            // ── Forecast vs Actual by Deal (จาก MGT_ForecastSnapshot ที่ capture ตอน deal ยังเปิดอยู่) ──
            var forecastVsActual = await GetForecastVsActualAsync(mainRows, ct);

            var availableSalesEmployeesRaw = await GetDistinctFieldAsync(filter, x => x.SalesEmployeeBP, lockedRawNames, ct);
            var availableCustomers = await GetDistinctFieldAsync(filter, x => x.CustomerName, lockedRawNames, ct);
            var availableIndustries = await GetDistinctFieldAsync(filter, x => x.IndustryName, lockedRawNames, ct);
            var availableProducts = await GetAvailableProductsAsync(filter.SalesGroup, lockedRawNames, ct);

            // ★ SalesEmployeeBP เก็บชื่อบางส่วนจาก Zoho — map เป็น Ms_User.FullName ก่อนแสดงผลทุกจุด
            var salesEmployeeNameMap = await _nameResolver.BuildNameMapAsync(
                bySalesperson.Select(r => r.GroupName)
                    .Concat(forecastedDeals.Select(r => r.SalesEmployeeBP))
                    .Concat(forecastVsActual.Select(r => r.SalesEmployeeBP))
                    .Concat(availableSalesEmployeesRaw), ct);

            string MapSalesEmployeeName(string? raw) =>
                !string.IsNullOrWhiteSpace(raw) && salesEmployeeNameMap.TryGetValue(raw, out var mapped) ? mapped : (raw ?? "");

            var availableSalesEmployeesMapped = availableSalesEmployeesRaw
                .Select(MapSalesEmployeeName)
                .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "Department", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            // ★ เมื่อ BU ถูกล็อก (filter.SalesGroup มีค่า, กรณีนี้คือ Leader) dropdown "Salesperson" แสดงเฉพาะพนักงานที่
            // Home Division (Ms_User.Division) ตรงกับ BU นี้เท่านั้น (ตาราง "By Salesperson" เองยังคงแสดงชื่อจริงทั้งหมด
            // ตามธุรกรรม ไม่กรอง/ไม่รวมกลุ่ม — ยืนยันแล้วว่าต้องการแบบนี้)
            var availableSalesEmployees = await _nameResolver.FilterToDivisionAsync(availableSalesEmployeesMapped, filter.SalesGroup, ct);

            foreach (var row in bySalesperson) row.GroupName = MapSalesEmployeeName(row.GroupName);
            foreach (var row in forecastedDeals) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);
            foreach (var row in forecastVsActual) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);

            return new SalesForecastAccuracyDto
            {
                Kpi = kpi,
                Trend = trend,
                BySalesperson = bySalesperson,
                ByCustomer = byCustomer,
                ByIndustry = byIndustry,
                ByProduct = byProduct,
                ForecastedDeals = forecastedDeals,
                ForecastVsActual = forecastVsActual,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableCustomers = availableCustomers,
                AvailableProducts = availableProducts,
                AvailableIndustries = availableIndustries
            };
        }

        private static ForecastTrendPointDto BuildTrendPoint(DateTime month, List<DealRow> rows)
        {
            var won = rows.Where(x => x.ForecastCategory == WonCategory).ToList();
            var lost = rows.Where(x => x.ForecastCategory == LostCategory).ToList();
            var pending = rows.Where(x => x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory).ToList();

            return new ForecastTrendPointDto
            {
                PeriodLabel = month.ToString("MMM yy"),
                TotalDeals = rows.Count,
                ForecastValue = rows.Sum(x => x.DealAmount ?? 0),
                WonDeals = won.Count,
                WonValue = won.Sum(x => x.DealAmount ?? 0),
                LostDeals = lost.Count,
                LostValue = lost.Sum(x => x.DealAmount ?? 0),
                PendingDeals = pending.Count,
                PendingValue = pending.Sum(x => x.DealAmount ?? 0)
            };
        }

        private static List<ForecastGroupRowDto> BuildGroupBreakdown(List<DealRow> rows, Func<DealRow, string?> keySelector) =>
            rows.Where(x => !string.IsNullOrWhiteSpace(keySelector(x)))
                .GroupBy(x => keySelector(x)!)
                .Select(g =>
                {
                    var won = g.Count(x => x.ForecastCategory == WonCategory);
                    var lost = g.Count(x => x.ForecastCategory == LostCategory);
                    var pending = g.Count(x => x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory);
                    var decided = won + lost;

                    return new ForecastGroupRowDto
                    {
                        GroupName = g.Key,
                        TotalDeals = g.Count(),
                        ForecastValue = g.Sum(x => x.DealAmount ?? 0),
                        WonDeals = won,
                        LostDeals = lost,
                        PendingDeals = pending,
                        WinRatePercent = decided == 0 ? null : Math.Round(won * 100m / decided, 1)
                    };
                })
                .OrderByDescending(x => x.ForecastValue)
                .ToList();

        private async Task<List<ForecastGroupRowDto>> GetByProductAsync(SalesForecastAccuracyFilter filter, List<string>? lockedRawNames, CancellationToken ct)
        {
            var scopeQuery = ForecastedDealsQuery(filter, lockedRawNames);
            if (filter.DateFrom.HasValue) scopeQuery = scopeQuery.Where(x => x.ClosingDate >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) scopeQuery = scopeQuery.Where(x => x.ClosingDate <= filter.DateTo.Value);

            var rows = await (
                from p in _db.MGT_DealProduct.AsNoTracking()
                join d in scopeQuery on p.DealId equals d.DealId
                select new { p.Product, d.DealAmount, d.ForecastCategory }
            ).ToListAsync(ct);

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Product))
                .GroupBy(x => x.Product!)
                .Select(g =>
                {
                    var won = g.Count(x => x.ForecastCategory == WonCategory);
                    var lost = g.Count(x => x.ForecastCategory == LostCategory);
                    var pending = g.Count(x => x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory);
                    var decided = won + lost;

                    return new ForecastGroupRowDto
                    {
                        GroupName = g.Key,
                        TotalDeals = g.Count(),
                        ForecastValue = g.Sum(x => x.DealAmount ?? 0),
                        WonDeals = won,
                        LostDeals = lost,
                        PendingDeals = pending,
                        WinRatePercent = decided == 0 ? null : Math.Round(won * 100m / decided, 1)
                    };
                })
                .OrderByDescending(x => x.ForecastValue)
                .ToList();
        }

        // ★ MGT_ForecastSnapshot เก็บ ForecastAmount ของ deal "ตอนยังเปิดอยู่" ทุกครั้งที่ sync (อาจมีหลายวันสะสม)
        // เอา snapshot ล่าสุดต่อ deal มาเทียบกับผลจริงตอนนี้ (Won = DealAmount ปัจจุบัน, Lost = 0, ยังเปิดอยู่ = ยังไม่รู้ผล)
        private async Task<List<ForecastVsActualRowDto>> GetForecastVsActualAsync(List<DealRow> mainRows, CancellationToken ct)
        {
            var dealIds = mainRows.Select(x => x.DealId).ToList();
            if (dealIds.Count == 0) return new();

            var snapshots = await _db.MGT_ForecastSnapshot.AsNoTracking()
                .Where(s => dealIds.Contains(s.DealId))
                .Select(s => new { s.DealId, s.ForecastAmount, s.SnapshotDate })
                .ToListAsync(ct);

            if (snapshots.Count == 0) return new();

            var latestByDeal = snapshots
                .GroupBy(s => s.DealId)
                .Select(g => g.OrderByDescending(s => s.SnapshotDate).First())
                .ToDictionary(s => s.DealId);

            return mainRows
                .Where(x => latestByDeal.ContainsKey(x.DealId))
                .Select(x =>
                {
                    var snap = latestByDeal[x.DealId];
                    decimal? actual = x.ForecastCategory switch
                    {
                        WonCategory => x.DealAmount ?? 0,
                        LostCategory => 0m,
                        _ => null
                    };

                    return new ForecastVsActualRowDto
                    {
                        DealId = x.DealId,
                        OpportunityName = x.OpportunityName ?? "",
                        CustomerName = x.CustomerName ?? "",
                        SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                        ForecastAmount = snap.ForecastAmount,
                        ActualAmount = actual,
                        Outcome = GetOutcome(x.ForecastCategory),
                        SnapshotDate = snap.SnapshotDate
                    };
                })
                .OrderByDescending(x => x.SnapshotDate)
                .ToList();
        }

        private static string GetOutcome(string? forecastCategory) => forecastCategory switch
        {
            WonCategory => "Won",
            LostCategory => "Lost",
            _ => "Pending"
        };

        private async Task<List<string>> GetDistinctFieldAsync(
            SalesForecastAccuracyFilter filter, Expression<Func<MGT_Deal, string?>> keySelector, List<string>? lockedRawNames, CancellationToken ct)
        {
            // dropdown scope ตาม SalesGroup เท่านั้น (ไม่ใส่ filter ของตัวเอง) เพื่อให้เห็นตัวเลือกครบหลังเลือกแล้ว
            var baseFilter = new SalesForecastAccuracyFilter { SalesGroup = filter.SalesGroup };
            var query = ForecastedDealsQuery(baseFilter, lockedRawNames);

            return await query
                .Select(keySelector)
                .Where(v => v != null && v != "")
                .Select(v => v!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync(ct);
        }

        private async Task<List<string>> GetAvailableProductsAsync(string? salesGroup, List<string>? lockedRawNames, CancellationToken ct)
        {
            var dealQuery = ForecastedDealsQuery(new SalesForecastAccuracyFilter { SalesGroup = salesGroup }, lockedRawNames);

            var query = from p in _db.MGT_DealProduct.AsNoTracking()
                        join d in dealQuery on p.DealId equals d.DealId
                        select p.Product;

            return await query
                .Where(v => v != null && v != "")
                .Select(v => v!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync(ct);
        }

        // ★ กรองเฉพาะ Forecast_Status = "Yes" เสมอ ตามที่รายงานนี้ต้องการ
        // BU กรองจากรายชื่อดิบ (Zoho partial name) ของพนักงานขาย Active ของ BU นั้น (lockedRawNames) แทน SalesGroup
        private IQueryable<MGT_Deal> ForecastedDealsQuery(SalesForecastAccuracyFilter filter, List<string>? lockedRawNames)
        {
            var query = _db.MGT_Deal.AsNoTracking().Where(x => x.ForecastStatus == ForecastYes);

            if (lockedRawNames is not null)
                query = query.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));

            // ★ filter.SalesEmployeeBP เป็น FullName ที่เลือกจาก dropdown (map แล้ว) แต่คอลัมน์จริงเก็บชื่อบางส่วนจาก Zoho
            // จึงเทียบแบบ substring แทน exact match (ดู SalesEmployeeNameResolver)
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP != null && filter.SalesEmployeeBP.Contains(x.SalesEmployeeBP));

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
                query = query.Where(x => x.CustomerName == filter.CustomerName);

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            if (!string.IsNullOrWhiteSpace(filter.Product))
                query = query.Where(x => _db.MGT_DealProduct.Any(p => p.DealId == x.DealId && p.Product == filter.Product));

            return query;
        }
    }
}
