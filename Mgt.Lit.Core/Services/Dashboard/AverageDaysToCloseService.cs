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
    public class AverageDaysToCloseService : IAverageDaysToCloseService
    {
        // Zoho forecast_category ของ Stage ที่แปลว่า Won (ดู CustomerChurnAnalysisService/OpportunityWinRateService — ใช้ตรรกะเดียวกัน)
        private const string WonCategory = "Closed";
        private const int MaxTrendMonths = 36; // กันลูปยาวเกินไปถ้าไม่ได้กรอง DateFrom/DateTo

        private readonly AppDbContext _db;

        public AverageDaysToCloseService(AppDbContext db) => _db = db;

        private sealed record DealRow(
            string DealId, string? OpportunityName, string? CustomerName, string? SalesEmployeeBP,
            string? IndustryName, decimal? DealAmount, DateTime? CreatedDate, DateTime? EffectiveClosedDate);

        public async Task<AverageDaysToCloseDto> GetAsync(AverageDaysToCloseFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var scopeQuery = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), filter);

            IQueryable<MGT_Deal> ClosedWonInRange(DateTime? from, DateTime? to)
            {
                var q = scopeQuery.Where(x => x.ForecastCategory == WonCategory);
                if (from.HasValue) q = q.Where(x => (x.ActualClosedDate ?? x.ClosingDate) >= from.Value);
                if (to.HasValue) q = q.Where(x => (x.ActualClosedDate ?? x.ClosingDate) <= to.Value);
                return q;
            }

            var mainRows = (await ClosedWonInRange(filter.DateFrom, filter.DateTo)
                .Select(x => new
                {
                    x.DealId,
                    x.OpportunityName,
                    x.CustomerName,
                    x.SalesEmployeeBP,
                    x.IndustryName,
                    x.DealAmount,
                    x.CreatedDate,
                    EffectiveClosedDate = x.ActualClosedDate ?? x.ClosingDate
                })
                .ToListAsync(ct))
                .Select(x => new DealRow(x.DealId, x.OpportunityName, x.CustomerName, x.SalesEmployeeBP,
                    x.IndustryName, x.DealAmount, x.CreatedDate, x.EffectiveClosedDate))
                .ToList();

            var closedWonDeals = mainRows.Count;

            // เฉพาะ Deal ที่มีทั้ง CreatedDate และวันปิดจริงครบ ถึงจะคำนวณ Days to Close ได้
            var withDays = mainRows
                .Where(x => x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                .Select(x => (Row: x, Days: (int)(x.EffectiveClosedDate!.Value.Date - x.CreatedDate!.Value.Date).TotalDays))
                .ToList();

            var daysList = withDays.Select(x => x.Days).ToList();

            double? avgDays = daysList.Count == 0 ? null : Math.Round(daysList.Average(), 1);
            double? medianDays = daysList.Count == 0 ? null : Math.Round(Median(daysList), 1);
            int? minDays = daysList.Count == 0 ? null : daysList.Min();
            int? maxDays = daysList.Count == 0 ? null : daysList.Max();

            var over90 = daysList.Count(d => d > 90);
            var over90Percent = closedWonDeals == 0 ? 0 : Math.Round(over90 * 100m / closedWonDeals, 1);

            var fastest = withDays.Count == 0 ? default : withDays.OrderBy(x => x.Days).First();
            var slowest = withDays.Count == 0 ? default : withDays.OrderByDescending(x => x.Days).First();

            // Longest Salesperson = MAX(AvgDaysBySalesperson) — ระดับค่าเฉลี่ยของพนักงาน ไม่ใช่ deal เดี่ยว (สเปคข้อ 8)
            var longestSalesperson = withDays
                .Where(x => !string.IsNullOrWhiteSpace(x.Row.SalesEmployeeBP))
                .GroupBy(x => x.Row.SalesEmployeeBP!)
                .Select(g => new { Name = g.Key, Avg = g.Average(x => x.Days) })
                .OrderByDescending(x => x.Avg)
                .FirstOrDefault();

            // ── Breakdown: Industry / Salesperson / Customer (จาก mainRows ชุดเดียวกัน) ─────
            var byIndustry = withDays
                .Where(x => !string.IsNullOrWhiteSpace(x.Row.IndustryName))
                .GroupBy(x => x.Row.IndustryName!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.Days).ToList()))
                .OrderByDescending(x => x.AvgDays)
                .ToList();

            var bySalesperson = withDays
                .Where(x => !string.IsNullOrWhiteSpace(x.Row.SalesEmployeeBP))
                .GroupBy(x => x.Row.SalesEmployeeBP!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.Days).ToList()))
                .OrderBy(x => x.AvgDays)
                .ToList();

            var byCustomer = withDays
                .Where(x => !string.IsNullOrWhiteSpace(x.Row.CustomerName))
                .GroupBy(x => x.Row.CustomerName!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.Days).ToList()))
                .OrderByDescending(x => x.AvgDays)
                .ToList();

            // ── Breakdown: Product (join ตาราง MGT_DealProduct — 1 Deal นับได้หลาย Product ตามที่ตั้งใจ) ──
            var byProduct = await GetByProductAsync(filter, ct);

            // ── By Status (เร็ว/ปานกลาง/เริ่มนาน/นาน) ────────────────────────────────
            var byStatus = new[] { "เร็ว", "ปานกลาง", "เริ่มนาน", "นาน" }.Select(status =>
            {
                var count = daysList.Count(d => GetStatus(d) == status);
                return new DaysToCloseStatusRowDto
                {
                    Status = status,
                    DealCount = count,
                    SharePercent = daysList.Count == 0 ? 0 : Math.Round(count * 100m / daysList.Count, 1)
                };
            }).ToList();

            // ── Trend รายเดือน ภายในช่วงที่กรอง (ถ้าไม่กรองช่วงเวลา ใช้ min-max ของข้อมูลที่มีจริงแทน) ──
            var trend = BuildTrend(withDays, filter.DateFrom, filter.DateTo);

            // ── เทียบกับช่วงก่อนหน้า ─────────────────────────────────────────────────
            var (prevAvg, prevClosedCount, prevOver90Percent) = await GetPreviousPeriodStatsAsync(filter, ClosedWonInRange, ct);

            // ── Recent Closed Won Opportunities (ล่าสุด 10 รายการ) ───────────────────
            var recentClosed = mainRows.OrderByDescending(x => x.EffectiveClosedDate).Take(10).ToList();
            var recentDealIds = recentClosed.Select(x => x.DealId).ToList();
            var productsByDeal = recentDealIds.Count == 0
                ? new Dictionary<string, string>()
                : (await _db.MGT_DealProduct.AsNoTracking()
                    .Where(p => recentDealIds.Contains(p.DealId))
                    .Select(p => new { p.DealId, p.Product })
                    .ToListAsync(ct))
                    .Where(p => !string.IsNullOrWhiteSpace(p.Product))
                    .GroupBy(p => p.DealId)
                    .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Product).Distinct()));

            var recentOpportunities = recentClosed.Select(x =>
            {
                int? days = (x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                    ? (int)(x.EffectiveClosedDate!.Value.Date - x.CreatedDate!.Value.Date).TotalDays
                    : null;

                return new ClosedWonOpportunityRowDto
                {
                    DealId = x.DealId,
                    OpportunityName = x.OpportunityName ?? "",
                    CustomerName = x.CustomerName ?? "",
                    SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                    Product = productsByDeal.TryGetValue(x.DealId, out var p) ? p : "",
                    IndustryName = x.IndustryName ?? "",
                    DealAmount = x.DealAmount ?? 0,
                    CreatedDate = x.CreatedDate,
                    ClosedWonDate = x.EffectiveClosedDate,
                    DaysToClose = days,
                    Status = days.HasValue ? GetStatus(days.Value) : ""
                };
            }).ToList();

            var availableSalesEmployees = await GetDistinctDealFieldAsync(filter, x => x.SalesEmployeeBP, ct);
            var availableIndustries = await GetDistinctDealFieldAsync(filter, x => x.IndustryName, ct);
            var availableCustomers = await GetDistinctDealFieldAsync(filter, x => x.CustomerName, ct);
            var availableProducts = await GetAvailableProductsAsync(filter.SalesGroup, ct);

            return new AverageDaysToCloseDto
            {
                Kpi = new DaysToCloseKpiDto
                {
                    AvgDaysToClose = avgDays,
                    MedianDaysToClose = medianDays,
                    MinDaysToClose = minDays,
                    MaxDaysToClose = maxDays,
                    ClosedWonDeals = closedWonDeals,
                    DealsOver90Days = over90,
                    DealsOver90DaysPercent = over90Percent,

                    FastestCustomerName = fastest.Row?.CustomerName,
                    FastestCustomerDays = withDays.Count == 0 ? null : fastest.Days,
                    SlowestCustomerName = slowest.Row?.CustomerName,
                    SlowestCustomerDays = withDays.Count == 0 ? null : slowest.Days,
                    LongestSalespersonName = longestSalesperson?.Name,
                    LongestSalespersonAvgDays = longestSalesperson is null ? null : Math.Round(longestSalesperson.Avg, 1),

                    AvgDaysChangeVsPrevious = (avgDays.HasValue && prevAvg.HasValue) ? Math.Round(avgDays.Value - prevAvg.Value, 1) : null,
                    AvgDaysImprovementPercent = (avgDays.HasValue && prevAvg.HasValue && prevAvg.Value != 0)
                        ? Math.Round((decimal)((prevAvg.Value - avgDays.Value) / prevAvg.Value * 100), 1) : null,
                    ClosedWonDealsChangeVsPrevious = closedWonDeals - prevClosedCount,
                    ClosedWonDealsChangePercent = prevClosedCount == 0 ? null : Math.Round((closedWonDeals - prevClosedCount) * 100m / prevClosedCount, 1),
                    DealsOver90DaysRateChangePercent = (prevOver90Percent.HasValue && prevOver90Percent.Value != 0)
                        ? Math.Round((over90Percent - prevOver90Percent.Value) * 100m / prevOver90Percent.Value, 1) : null
                },
                Trend = trend,
                ByStatus = byStatus,
                ByIndustry = byIndustry,
                ByProduct = byProduct,
                BySalesperson = bySalesperson,
                ByCustomer = byCustomer,
                RecentClosedWonOpportunities = recentOpportunities,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableProducts = availableProducts,
                AvailableIndustries = availableIndustries,
                AvailableCustomers = availableCustomers
            };
        }

        private async Task<(double? PrevAvg, int PrevClosedCount, decimal? PrevOver90Percent)> GetPreviousPeriodStatsAsync(
            AverageDaysToCloseFilter filter, Func<DateTime?, DateTime?, IQueryable<MGT_Deal>> closedWonInRange, CancellationToken ct)
        {
            if (!filter.DateFrom.HasValue || !filter.DateTo.HasValue)
                return (null, 0, null);

            var length = filter.DateTo.Value - filter.DateFrom.Value;
            var prevTo = filter.DateFrom.Value.AddDays(-1);
            var prevFrom = prevTo - length;

            var prevRows = await closedWonInRange(prevFrom, prevTo)
                .Select(x => new { x.CreatedDate, EffectiveClosedDate = x.ActualClosedDate ?? x.ClosingDate })
                .ToListAsync(ct);

            var prevDays = prevRows
                .Where(x => x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                .Select(x => (int)(x.EffectiveClosedDate!.Value.Date - x.CreatedDate!.Value.Date).TotalDays)
                .ToList();

            double? prevAvg = prevDays.Count == 0 ? null : Math.Round(prevDays.Average(), 1);
            var prevOver90 = prevDays.Count(d => d > 90);
            decimal? prevOver90Percent = prevRows.Count == 0 ? null : Math.Round(prevOver90 * 100m / prevRows.Count, 1);

            return (prevAvg, prevRows.Count, prevOver90Percent);
        }

        private static List<DaysToCloseTrendPointDto> BuildTrend(
            List<(DealRow Row, int Days)> withDays, DateTime? dateFrom, DateTime? dateTo)
        {
            if (withDays.Count == 0) return new();

            var start = dateFrom ?? withDays.Min(x => x.Row.EffectiveClosedDate!.Value);
            var end = dateTo ?? withDays.Max(x => x.Row.EffectiveClosedDate!.Value);

            var cursor = new DateTime(start.Year, start.Month, 1);
            var endMonth = new DateTime(end.Year, end.Month, 1);

            var trend = new List<DaysToCloseTrendPointDto>();
            var guard = 0;
            while (cursor <= endMonth && guard < MaxTrendMonths)
            {
                var monthRows = withDays.Where(x => x.Row.EffectiveClosedDate!.Value.Year == cursor.Year
                                                  && x.Row.EffectiveClosedDate!.Value.Month == cursor.Month).ToList();

                trend.Add(new DaysToCloseTrendPointDto
                {
                    PeriodLabel = cursor.ToString("MMM yy"),
                    ClosedWonDeals = monthRows.Count,
                    AvgDaysToClose = monthRows.Count == 0 ? null : Math.Round(monthRows.Average(x => x.Days), 1)
                });

                cursor = cursor.AddMonths(1);
                guard++;
            }
            return trend;
        }

        private async Task<List<DaysToCloseGroupRowDto>> GetByProductAsync(AverageDaysToCloseFilter filter, CancellationToken ct)
        {
            var scopeQuery = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), filter).Where(x => x.ForecastCategory == WonCategory);

            if (filter.DateFrom.HasValue) scopeQuery = scopeQuery.Where(x => (x.ActualClosedDate ?? x.ClosingDate) >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) scopeQuery = scopeQuery.Where(x => (x.ActualClosedDate ?? x.ClosingDate) <= filter.DateTo.Value);

            var rows = await (
                from p in _db.MGT_DealProduct.AsNoTracking()
                join d in scopeQuery on p.DealId equals d.DealId
                select new { p.Product, d.CreatedDate, EffectiveClosedDate = d.ActualClosedDate ?? d.ClosingDate }
            ).ToListAsync(ct);

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Product) && x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                .GroupBy(x => x.Product!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => (int)(x.EffectiveClosedDate!.Value.Date - x.CreatedDate!.Value.Date).TotalDays).ToList()))
                .OrderByDescending(x => x.AvgDays)
                .Take(10)
                .ToList();
        }

        private static DaysToCloseGroupRowDto BuildGroupRow(string name, List<int> days)
        {
            var avg = days.Average();
            return new DaysToCloseGroupRowDto
            {
                GroupName = name,
                ClosedWonDeals = days.Count,
                AvgDays = Math.Round(avg, 1),
                MinDays = days.Min(),
                MaxDays = days.Max(),
                Status = GetStatus(avg)
            };
        }

        // เกณฑ์ตามสเปคข้อ 11: ≤30 เร็ว, 31-70 ปานกลาง, 71-90 เริ่มนาน, >90 นาน
        private static string GetStatus(double avgDays) =>
            avgDays <= 30 ? "เร็ว" :
            avgDays <= 70 ? "ปานกลาง" :
            avgDays <= 90 ? "เริ่มนาน" : "นาน";

        private static double Median(List<int> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        private async Task<List<string>> GetDistinctDealFieldAsync(
            AverageDaysToCloseFilter filter, Expression<Func<MGT_Deal, string?>> keySelector, CancellationToken ct)
        {
            // dropdown scope ตาม SalesGroup เท่านั้น (ไม่ใส่ filter ของตัวเอง) เพื่อให้เห็นตัวเลือกครบหลังเลือกแล้ว
            var baseFilter = new AverageDaysToCloseFilter { SalesGroup = filter.SalesGroup };
            var query = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), baseFilter);

            return await query
                .Select(keySelector)
                .Where(v => v != null && v != "")
                .Select(v => v!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync(ct);
        }

        private async Task<List<string>> GetAvailableProductsAsync(string? salesGroup, CancellationToken ct)
        {
            var dealQuery = string.IsNullOrWhiteSpace(salesGroup)
                ? _db.MGT_Deal.AsNoTracking()
                : _db.MGT_Deal.AsNoTracking().Where(x => x.SalesGroup == salesGroup);

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

        // instance method เพราะ filter ตาม Product ต้องอ้าง _db.MGT_DealProduct (subquery)
        private IQueryable<MGT_Deal> ApplyScopeFilter(IQueryable<MGT_Deal> query, AverageDaysToCloseFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.SalesGroup))
                query = query.Where(x => x.SalesGroup == filter.SalesGroup);

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
                query = query.Where(x => x.CustomerName == filter.CustomerName);

            // ★ cross-filter จากการเลือก Product — Deal ต้องมี Product นี้อยู่ในรายการสินค้าของตัวเอง (subquery ตาราง MGT_DealProduct)
            if (!string.IsNullOrWhiteSpace(filter.Product))
                query = query.Where(x => _db.MGT_DealProduct.Any(p => p.DealId == x.DealId && p.Product == filter.Product));

            return query;
        }
    }
}
