using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class SalesProductivityService : ISalesProductivityService
    {
        // Zoho forecast_category ของ Stage (Pipeline/Closed/Omitted) — ดู ZohoDealSyncService/OpportunityWinRateService
        private const string WonCategory = "Closed";
        private const string LostCategory = "Omitted";
        private const string PipelineCategory = "Pipeline";

        // ★ ชื่อ Stage แรกสุดของ pipeline องค์กรนี้จริง (ยืนยันผ่าน settings/fields) — Opportunity ที่ยังอยู่ Stage นี้
        // ถือว่ายัง "ไม่ผ่านการ Qualify" ตาม spec ข้อ 7 (ไม่มี field "Qualification Status" แยกต่างหากใน Zoho ขององค์กรนี้)
        private const string UnqualifiedStage = "Draft/Qualification";

        private const int MaxTrendMonths = 36;

        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public SalesProductivityService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private sealed record DealRow(
            string DealId, string? OpportunityName, string? Stage, string? ForecastCategory, decimal? DealAmount,
            DateTime? CreatedDate, DateTime? ClosingDate, string? SalesEmployeeBP, string? CustomerName, string? LostReason);

        public async Task<SalesProductivityDto> GetAsync(SalesProductivityFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งรายงาน — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedRawNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeRawDealNamesAsync(filter.SalesGroup, ct);

            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var scopeQuery = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), filter, lockedRawNames);

            // ★ Cohort หลักของทั้งรายงาน: Opportunity ที่ถูก "สร้าง" ในช่วงที่กรอง (ไม่ใช่ปิดในช่วงนี้) — ตาม spec ข้อ 2
            IQueryable<MGT_Deal> CreatedInRange(DateTime? from, DateTime? to)
            {
                var q = scopeQuery;
                if (from.HasValue) q = q.Where(x => x.CreatedDate >= from.Value);
                if (to.HasValue) q = q.Where(x => x.CreatedDate <= to.Value);
                return q;
            }

            var mainRows = (await CreatedInRange(filter.DateFrom, filter.DateTo)
                .Select(x => new
                {
                    x.DealId,
                    x.OpportunityName,
                    x.Stage,
                    x.ForecastCategory,
                    x.DealAmount,
                    x.CreatedDate,
                    x.ClosingDate,
                    x.SalesEmployeeBP,
                    x.CustomerName,
                    x.LostReason
                })
                .ToListAsync(ct))
                .Select(x => new DealRow(x.DealId, x.OpportunityName, x.Stage, x.ForecastCategory, x.DealAmount,
                    x.CreatedDate, x.ClosingDate, x.SalesEmployeeBP, x.CustomerName, x.LostReason))
                .ToList();

            var totalOpportunities = mainRows.Count;
            var won = mainRows.Where(x => x.ForecastCategory == WonCategory).ToList();
            var lost = mainRows.Where(x => x.ForecastCategory == LostCategory).ToList();
            var pipeline = mainRows.Where(x => x.ForecastCategory == PipelineCategory).ToList();
            var qualified = mainRows.Where(x => x.Stage != UnqualifiedStage).ToList();
            var closedCount = won.Count + lost.Count;

            var activeSalespersons = mainRows.Select(x => x.SalesEmployeeBP).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count();
            var avgOppPerSalesperson = activeSalespersons == 0 ? 0 : Math.Round((double)totalOpportunities / activeSalespersons, 1);

            var revenue = won.Sum(x => x.DealAmount ?? 0);
            var pipelineValue = pipeline.Sum(x => x.DealAmount ?? 0);

            decimal? overallWinRate = closedCount == 0 ? null : Math.Round(won.Count * 100m / closedCount, 1);

            var kpi = new SalesProductivityKpiDto
            {
                TotalOpportunities = totalOpportunities,
                ActiveSalespersons = activeSalespersons,
                AvgOpportunityPerSalesperson = avgOppPerSalesperson,
                QualifiedOpportunities = qualified.Count,
                QualifiedOpportunityRatePercent = totalOpportunities == 0 ? 0 : Math.Round(qualified.Count * 100m / totalOpportunities, 1),
                WonOpportunities = won.Count,
                LostOpportunities = lost.Count,
                ClosedOpportunities = closedCount,
                OverallWinRatePercent = overallWinRate,
                ConversionRatePercent = totalOpportunities == 0 ? 0 : Math.Round(won.Count * 100m / totalOpportunities, 1),
                RevenueClosedWon = revenue,
                RevenuePerOpportunity = totalOpportunities == 0 ? 0 : Math.Round(revenue / totalOpportunities, 2),
                AvgWonDealSize = won.Count == 0 ? 0 : Math.Round(revenue / won.Count, 2),
                PipelineValue = pipelineValue
            };

            kpi.TotalOpportunitiesChangePercent = await GetPreviousPeriodOppChangeAsync(filter, CreatedInRange, totalOpportunities, ct);

            // ── Trend รายเดือน (ตาม CreatedDate) ────────────────────────────────────
            var trend = BuildTrend(mainRows, filter.DateFrom, filter.DateTo);

            // ── Pipeline by current Stage (เฉพาะ cohort นี้ที่ยังเปิดอยู่) ───────────────
            var pipelineByStage = pipeline
                .Where(x => !string.IsNullOrWhiteSpace(x.Stage))
                .GroupBy(x => x.Stage!)
                .Select(g => new StageFunnelRowDto { Stage = g.Key, DealCount = g.Count() })
                .OrderByDescending(x => x.DealCount)
                .ToList();
            pipelineByStage.Add(new StageFunnelRowDto { Stage = "Closed Won", DealCount = won.Count });
            pipelineByStage.Add(new StageFunnelRowDto { Stage = "Closed Lost", DealCount = lost.Count });

            // ★ SalesEmployeeBP เก็บชื่อบางส่วนจาก Zoho — map เป็น Ms_User.FullName ก่อน แล้วค่อย group เป็น "By Salesperson"
            var availableSalesEmployeesRaw = await GetDistinctDealFieldAsync(filter, x => x.SalesEmployeeBP, lockedRawNames, ct);
            var salesEmployeeNameMap = await _nameResolver.BuildNameMapAsync(
                mainRows.Select(x => x.SalesEmployeeBP).Concat(availableSalesEmployeesRaw), ct);

            string MapSalesEmployeeName(string? raw) =>
                !string.IsNullOrWhiteSpace(raw) && salesEmployeeNameMap.TryGetValue(raw, out var mapped) ? mapped : (raw ?? "");

            // ── By Salesperson (ตารางหลัก) ───────────────────────────────────────────
            var avgRevenuePerSalesperson = activeSalespersons == 0 ? 0 : revenue / activeSalespersons;
            var bySalesperson = mainRows
                .Where(x => !string.IsNullOrWhiteSpace(x.SalesEmployeeBP))
                .GroupBy(x => MapSalesEmployeeName(x.SalesEmployeeBP))
                .Select(g => BuildSalespersonRow(g.Key, g.ToList(), avgOppPerSalesperson, overallWinRate, avgRevenuePerSalesperson))
                .OrderByDescending(x => x.Opportunities)
                .ToList();

            // ── Lost Reasons ──────────────────────────────────────────────────────────
            var lostReasons = lost
                .GroupBy(x => string.IsNullOrWhiteSpace(x.LostReason) ? "Unspecified" : x.LostReason!)
                .Select(g => new LostReasonRowDto
                {
                    Reason = g.Key,
                    LostDeals = g.Count(),
                    SharePercent = lost.Count == 0 ? 0 : Math.Round(g.Count() * 100m / lost.Count, 1)
                })
                .OrderByDescending(x => x.LostDeals)
                .ToList();

            // ── Recent Closed Won (ล่าสุด 5 รายการ) ──────────────────────────────────
            var recentClosedWonDeals = won.OrderByDescending(x => x.ClosingDate).Take(5).ToList();
            var recentClosedWon = recentClosedWonDeals.Select(x => new ClosedOpportunityRowDto
            {
                DealId = x.DealId,
                OpportunityName = x.OpportunityName ?? "",
                CustomerName = x.CustomerName ?? "",
                SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                DealAmount = x.DealAmount ?? 0,
                ClosedDate = x.ClosingDate,
                Result = "Won"
            }).ToList();

            // ── Open Pipeline Top 5 by Value ─────────────────────────────────────────
            var openPipelineTop = pipeline
                .OrderByDescending(x => x.DealAmount ?? 0)
                .Take(5)
                .Select(x => new PipelineDealRowDto
                {
                    DealId = x.DealId,
                    OpportunityName = x.OpportunityName ?? "",
                    CustomerName = x.CustomerName ?? "",
                    SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                    Stage = x.Stage ?? "",
                    DealAmount = x.DealAmount ?? 0,
                    ClosingDate = x.ClosingDate
                })
                .ToList();

            var availableStages = await GetDistinctDealFieldAsync(filter, x => x.Stage, lockedRawNames, ct);
            var availableCustomers = await GetDistinctDealFieldAsync(filter, x => x.CustomerName, lockedRawNames, ct);
            var availableProducts = await GetAvailableProductsAsync(filter.SalesGroup, lockedRawNames, ct);

            var availableSalesEmployeesMapped = availableSalesEmployeesRaw
                .Select(MapSalesEmployeeName)
                .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "Department", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            var availableSalesEmployees = await _nameResolver.FilterToDivisionAsync(availableSalesEmployeesMapped, filter.SalesGroup, ct);

            foreach (var row in recentClosedWon) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);
            foreach (var row in openPipelineTop) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);

            return new SalesProductivityDto
            {
                Kpi = kpi,
                Trend = trend,
                PipelineByStage = pipelineByStage,
                BySalesperson = bySalesperson,
                LostReasons = lostReasons,
                RecentClosedWon = recentClosedWon,
                OpenPipelineTop = openPipelineTop,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableStages = availableStages,
                AvailableProducts = availableProducts,
                AvailableCustomers = availableCustomers
            };
        }

        private static SalesProductivityRowDto BuildSalespersonRow(
            string name, List<DealRow> rows, double avgOppPerSalesperson, decimal? overallWinRate, decimal avgRevenuePerSalesperson)
        {
            var won = rows.Count(x => x.ForecastCategory == WonCategory);
            var lost = rows.Count(x => x.ForecastCategory == LostCategory);
            var closed = won + lost;
            var qualified = rows.Count(x => x.Stage != UnqualifiedStage);
            var pipelineValue = rows.Where(x => x.ForecastCategory == PipelineCategory).Sum(x => x.DealAmount ?? 0);
            var revenue = rows.Where(x => x.ForecastCategory == WonCategory).Sum(x => x.DealAmount ?? 0);
            decimal? winRate = closed == 0 ? null : Math.Round(won * 100m / closed, 1);
            var revPerOpp = rows.Count == 0 ? 0 : Math.Round(revenue / rows.Count, 2);
            var productivityIndex = avgOppPerSalesperson == 0 ? 0 : Math.Round(rows.Count * 100m / (decimal)avgOppPerSalesperson, 1);

            string status;
            if (closed == 0)
            {
                status = "N/A";
            }
            else
            {
                var highOpp = rows.Count >= avgOppPerSalesperson;
                var highWinRate = winRate.HasValue && overallWinRate.HasValue && winRate.Value >= overallWinRate.Value;
                var highRevenue = revenue >= avgRevenuePerSalesperson;
                var score = (highOpp ? 1 : 0) + (highWinRate ? 1 : 0) + (highRevenue ? 1 : 0);
                status = score switch
                {
                    3 => "Excellent",
                    2 => "Good",
                    1 => "Watch",
                    _ => "Needs Improvement"
                };
            }

            return new SalesProductivityRowDto
            {
                SalesEmployeeBP = name,
                Opportunities = rows.Count,
                Qualified = qualified,
                Won = won,
                Lost = lost,
                Closed = closed,
                WinRatePercent = winRate,
                PipelineValue = pipelineValue,
                Revenue = revenue,
                RevenuePerOpportunity = revPerOpp,
                ProductivityIndexPercent = productivityIndex,
                Status = status
            };
        }

        private async Task<decimal?> GetPreviousPeriodOppChangeAsync(
            SalesProductivityFilter filter, Func<DateTime?, DateTime?, IQueryable<MGT_Deal>> createdInRange, int currentCount, CancellationToken ct)
        {
            if (!filter.DateFrom.HasValue || !filter.DateTo.HasValue) return null;

            var length = filter.DateTo.Value - filter.DateFrom.Value;
            var prevTo = filter.DateFrom.Value.AddDays(-1);
            var prevFrom = prevTo - length;

            var prevCount = await createdInRange(prevFrom, prevTo).CountAsync(ct);
            if (prevCount == 0) return null;
            return Math.Round((currentCount - prevCount) * 100m / prevCount, 1);
        }

        private static List<SalesProductivityTrendPointDto> BuildTrend(List<DealRow> rows, DateTime? dateFrom, DateTime? dateTo)
        {
            var withDate = rows.Where(x => x.CreatedDate.HasValue).ToList();
            if (withDate.Count == 0) return new();

            var start = dateFrom ?? withDate.Min(x => x.CreatedDate!.Value);
            var end = dateTo ?? withDate.Max(x => x.CreatedDate!.Value);

            var cursor = new DateTime(start.Year, start.Month, 1);
            var endMonth = new DateTime(end.Year, end.Month, 1);

            var trend = new List<SalesProductivityTrendPointDto>();
            var guard = 0;
            while (cursor <= endMonth && guard < MaxTrendMonths)
            {
                var monthRows = withDate.Where(x => x.CreatedDate!.Value.Year == cursor.Year && x.CreatedDate!.Value.Month == cursor.Month).ToList();

                trend.Add(new SalesProductivityTrendPointDto
                {
                    PeriodLabel = cursor.ToString("MMM yy"),
                    NewOpportunities = monthRows.Count,
                    WonDeals = monthRows.Count(x => x.ForecastCategory == WonCategory),
                    LostDeals = monthRows.Count(x => x.ForecastCategory == LostCategory)
                });

                cursor = cursor.AddMonths(1);
                guard++;
            }
            // แสดงเฉพาะเดือนที่มีข้อมูลจริง (ตัดเดือนที่ไม่มี opportunity เลยออก ไม่ให้กราฟรกด้วยเดือนว่าง)
            return trend.Where(x => x.NewOpportunities > 0).ToList();
        }

        private async Task<List<string>> GetDistinctDealFieldAsync(
            SalesProductivityFilter filter, System.Linq.Expressions.Expression<Func<MGT_Deal, string?>> keySelector, List<string>? lockedRawNames, CancellationToken ct)
        {
            // dropdown scope ตาม SalesGroup เท่านั้น (ไม่ใส่ filter ของตัวเอง) เพื่อให้เห็นตัวเลือกครบหลังเลือกแล้ว
            var baseFilter = new SalesProductivityFilter { SalesGroup = filter.SalesGroup };
            var query = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), baseFilter, lockedRawNames);

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
            var dealQuery = _db.MGT_Deal.AsNoTracking();
            if (lockedRawNames is not null)
                dealQuery = dealQuery.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));

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
        // ★ BU กรองจากรายชื่อดิบ (Zoho partial name) ของพนักงานขาย Active ของ BU นั้น (lockedRawNames) แทน SalesGroup
        private IQueryable<MGT_Deal> ApplyScopeFilter(IQueryable<MGT_Deal> query, SalesProductivityFilter filter, List<string>? lockedRawNames)
        {
            if (lockedRawNames is not null)
                query = query.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));

            // ★ filter.SalesEmployeeBP เป็น FullName ที่เลือกจาก dropdown (map แล้ว) แต่คอลัมน์จริงเก็บชื่อบางส่วนจาก Zoho
            // จึงเทียบแบบ substring แทน exact match (ดู SalesEmployeeNameResolver)
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP != null && filter.SalesEmployeeBP.Contains(x.SalesEmployeeBP));

            if (!string.IsNullOrWhiteSpace(filter.Stage))
                query = query.Where(x => x.Stage == filter.Stage);

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
                query = query.Where(x => x.CustomerName == filter.CustomerName);

            if (!string.IsNullOrWhiteSpace(filter.Product))
                query = query.Where(x => _db.MGT_DealProduct.Any(p => p.DealId == x.DealId && p.Product == filter.Product));

            return query;
        }
    }
}
