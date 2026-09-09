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
    public class OpportunityWinRateService : IOpportunityWinRateService
    {
        // Zoho จัดกลุ่ม Stage เองผ่าน forecast_category ของแต่ละ Stage picklist value:
        // "Closed" = Won, "Omitted" = Lost/Cancelled, "Pipeline" = ยังเปิดอยู่ — sync ไว้แล้วในคอลัมน์ ForecastCategory
        // (ชื่อ Stage จริงของแต่ละองค์กรไม่ตรงกับ "Closed Won"/"Closed Lost" เป๊ะๆ จึงห้ามเทียบชื่อ Stage ตรงๆ)
        private const string WonCategory = "Closed";
        private const string LostCategory = "Omitted";

        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public OpportunityWinRateService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private sealed record DealRow(
            string DealId, string? ForecastCategory, decimal? DealAmount, DateTime? CreatedDate, DateTime? EffectiveClosedDate,
            string? SalesEmployeeBP, string? IndustryName, string? LostReason, string? OpportunityName, string? CustomerName, string? Stage);

        public async Task<OpportunityWinRateDto> GetAsync(OpportunityWinRateFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งรายงาน — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            // คืนเป็น "ชื่อดิบ" ที่เจอจริงใน MGT_Deal.SalesEmployeeBP เพราะคอลัมน์นี้เก็บชื่อบางส่วนจาก Zoho ไม่ใช่ชื่อเต็ม
            var lockedRawNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeRawDealNamesAsync(filter.SalesGroup, ct);

            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var scopeQuery = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), filter, lockedRawNames);

            IQueryable<MGT_Deal> ClosedInRange(DateTime? from, DateTime? to)
            {
                var q = scopeQuery.Where(x => x.ForecastCategory == WonCategory || x.ForecastCategory == LostCategory);
                if (from.HasValue) q = q.Where(x => (x.ActualClosedDate ?? x.ClosingDate) >= from.Value);
                if (to.HasValue) q = q.Where(x => (x.ActualClosedDate ?? x.ClosingDate) <= to.Value);
                return q;
            }

            // ── ข้อมูลหลัก: Deal ที่ปิดผลแล้วภายในช่วงที่กรอง (ดึงครั้งเดียวใช้ซ้ำทุกส่วน) ──
            var mainRows = (await ClosedInRange(filter.DateFrom, filter.DateTo)
                .Select(x => new
                {
                    x.DealId,
                    x.ForecastCategory,
                    x.DealAmount,
                    x.CreatedDate,
                    EffectiveClosedDate = x.ActualClosedDate ?? x.ClosingDate,
                    x.SalesEmployeeBP,
                    x.IndustryName,
                    x.LostReason,
                    x.OpportunityName,
                    x.CustomerName,
                    x.Stage
                })
                .ToListAsync(ct))
                .Select(x => new DealRow(x.DealId, x.ForecastCategory, x.DealAmount, x.CreatedDate, x.EffectiveClosedDate,
                    x.SalesEmployeeBP, x.IndustryName, x.LostReason, x.OpportunityName, x.CustomerName, x.Stage))
                .ToList();

            var won = mainRows.Where(x => IsWon(x.ForecastCategory)).ToList();
            var lost = mainRows.Where(x => IsLost(x.ForecastCategory)).ToList();
            var totalClosed = mainRows.Count;

            var wonValue = won.Sum(x => x.DealAmount ?? 0);
            var lostValue = lost.Sum(x => x.DealAmount ?? 0);

            decimal? winRate = totalClosed == 0 ? null : Math.Round(won.Count * 100m / totalClosed, 1);
            decimal? lossRate = totalClosed == 0 ? null : Math.Round(lost.Count * 100m / totalClosed, 1);
            decimal? valueWinRate = (wonValue + lostValue) == 0 ? null : Math.Round(wonValue * 100m / (wonValue + lostValue), 1);

            var avgWonDealSize = won.Count == 0 ? 0 : Math.Round(wonValue / won.Count, 2);
            var avgLostDealSize = lost.Count == 0 ? 0 : Math.Round(lostValue / lost.Count, 2);

            var wonCycleDays = won.Where(x => x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                .Select(x => (x.EffectiveClosedDate!.Value - x.CreatedDate!.Value).TotalDays).ToList();
            var lostCycleDays = lost.Where(x => x.CreatedDate.HasValue && x.EffectiveClosedDate.HasValue)
                .Select(x => (x.EffectiveClosedDate!.Value - x.CreatedDate!.Value).TotalDays).ToList();
            double? avgCycleWon = wonCycleDays.Count == 0 ? null : Math.Round(wonCycleDays.Average(), 1);
            double? avgCycleLost = lostCycleDays.Count == 0 ? null : Math.Round(lostCycleDays.Average(), 1);

            // ── เทียบกับช่วงก่อนหน้า (ความยาวช่วงเวลาเท่ากัน ต่อท้ายกันทันที ก่อน DateFrom) ──
            var (prevWonCount, prevClosedCount, prevWinRate, prevValueWinRate, prevAvgWonDealSize) =
                await GetPreviousPeriodStatsAsync(filter, ClosedInRange, ct);

            // ── Monthly Trend: 12 เดือนล่าสุด นับถึง DateTo (หรือวันนี้) ไม่ผูกกับ DateFrom เพื่อให้เห็นเทรนด์มีบริบท ──
            var trend = await GetTrendAsync(filter, ClosedInRange, ct);

            // ── Breakdown: Salesperson / Industry (จาก mainRows ชุดเดียวกัน) ─────────────
            var bySalesperson = mainRows
                .Where(x => !string.IsNullOrWhiteSpace(x.SalesEmployeeBP))
                .GroupBy(x => x.SalesEmployeeBP!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.ForecastCategory).ToList()))
                .OrderByDescending(x => x.ClosedDeals)
                .ToList();

            var byIndustry = mainRows
                .Where(x => !string.IsNullOrWhiteSpace(x.IndustryName))
                .GroupBy(x => x.IndustryName!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.ForecastCategory).ToList()))
                .OrderByDescending(x => x.ClosedDeals)
                .ToList();

            // ── Breakdown: Product (join ตาราง MGT_DealProduct — 1 Deal นับได้หลาย Product ตามที่ตั้งใจ) ──
            var byProduct = await GetByProductAsync(filter, lockedRawNames, ct);

            // ── Pipeline by current Stage: deal ที่ยังเปิด (ไม่ผูกกับ date range) + deal ที่ปิดในช่วงที่กรอง
            // แสดงตาม Stage จริงแต่ละค่า (ไม่ยุบรวมเป็น "Closed Won"/"Closed Lost") เช่น
            // "Closed Won - Exact Match", "Closed Won - Over Forecast", "Close Lost - Expiry Date", "Cancelled" ฯลฯ
            var pipeline = await GetPipelineAsync(scopeQuery, ct);
            var closedByStage = mainRows
                .Where(x => !string.IsNullOrWhiteSpace(x.Stage))
                .GroupBy(x => x.Stage!)
                .Select(g => new StageFunnelRowDto { Stage = g.Key, DealCount = g.Count() });
            pipeline.AddRange(closedByStage);

            // ── Closed Opportunity Detail (แสดงข้อมูลทั้งหมดในช่วงที่เลือก ไม่จำกัดจำนวน) ─────
            var recentClosed = mainRows
                .OrderByDescending(x => x.EffectiveClosedDate)
                .ToList();
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

            var recentOpportunities = recentClosed.Select(x => new ClosedOpportunityRowDto
            {
                DealId = x.DealId,
                OpportunityName = x.OpportunityName ?? "",
                CustomerName = x.CustomerName ?? "",
                SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                Product = productsByDeal.TryGetValue(x.DealId, out var p) ? p : "",
                IndustryName = x.IndustryName ?? "",
                DealAmount = x.DealAmount ?? 0,
                ClosedDate = x.EffectiveClosedDate,
                Result = IsWon(x.ForecastCategory) ? "Won" : "Lost",
                LostReason = IsLost(x.ForecastCategory) ? x.LostReason : null
            }).ToList();

            // ── Open Opportunity Detail (ยังไม่ปิดผลทุกสถานะ ไม่ผูกกับ date range เหมือน openCount ด้านล่าง) ──
            var openRows = await scopeQuery
                .Where(x => x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory)
                .Select(x => new
                {
                    x.DealId,
                    x.OpportunityName,
                    x.CustomerName,
                    x.SalesEmployeeBP,
                    x.IndustryName,
                    x.DealAmount,
                    x.ClosingDate,
                    x.DeliveryDate,
                    x.Stage
                })
                .ToListAsync(ct);

            var openDealIds = openRows.Select(x => x.DealId).ToList();
            var productsByOpenDeal = openDealIds.Count == 0
                ? new Dictionary<string, string>()
                : (await _db.MGT_DealProduct.AsNoTracking()
                    .Where(p => openDealIds.Contains(p.DealId))
                    .Select(p => new { p.DealId, p.Product })
                    .ToListAsync(ct))
                    .Where(p => !string.IsNullOrWhiteSpace(p.Product))
                    .GroupBy(p => p.DealId)
                    .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Product).Distinct()));

            var openOpportunities = openRows
                .OrderBy(x => x.ClosingDate)   // ใกล้วันคาดปิดก่อน
                .Select(x => new OpenOpportunityRowDto
                {
                    DealId = x.DealId,
                    OpportunityName = x.OpportunityName ?? "",
                    CustomerName = x.CustomerName ?? "",
                    SalesEmployeeBP = x.SalesEmployeeBP ?? "",
                    Product = productsByOpenDeal.TryGetValue(x.DealId, out var op) ? op : "",
                    IndustryName = x.IndustryName ?? "",
                    DealAmount = x.DealAmount ?? 0,
                    ExpectedCloseDate = x.ClosingDate,
                    DeliveryDate = x.DeliveryDate,
                    Stage = x.Stage ?? ""
                }).ToList();

            // ── Summary panel: Open ไม่ผูกกับ date range (deal เปิดอยู่ยังไม่มีวันปิด) ────
            var openCount = openRows.Count;
            var summary = new WinRateSummaryDto
            {
                OpenOpportunities = openCount,
                ClosedOpportunities = totalClosed,
                TotalOpportunities = openCount + totalClosed,
                WonOpportunities = won.Count,
                LostOpportunities = lost.Count,
                WonValue = wonValue,
                LostValue = lostValue,
                TotalClosedValue = wonValue + lostValue,
                ValueWinRatePercent = valueWinRate
            };

            var availableSalesEmployeesRaw = await GetDistinctDealFieldAsync(filter, x => x.SalesEmployeeBP, lockedRawNames, ct);
            var availableIndustries = await GetDistinctDealFieldAsync(filter, x => x.IndustryName, lockedRawNames, ct);
            var availableProducts = await GetAvailableProductsAsync(lockedRawNames, ct);

            // ★ MGT_Deal.SalesEmployeeBP มาจาก Zoho Owner.name ซึ่งองค์กรนี้ตั้งเป็นแค่บางส่วนของชื่อ (เช่น "Dooduang"
            // แทนที่จะเป็น "Pattamawan Dooduang") — map เป็นชื่อเต็มจาก Ms_User (ตาราง user ภายใน) เพื่อแสดงผล/ให้เลือกกรอง
            // ด้วยชื่อเต็มแทน จับคู่แบบ "ชื่อเต็ม contains ค่าที่ตั้งใน Zoho" เพราะไม่มี key เชื่อมสองระบบตรงๆ
            // ⚠️ ข้อจำกัดที่ทราบ: ถ้ามี 2 คนที่นามสกุล/ชื่อบางส่วนซ้ำกัน อาจจับคู่ผิดคนได้ — ยอมรับ trade-off นี้ไว้ก่อน
            var salesEmployeeNameMap = await _nameResolver.BuildNameMapAsync(
                mainRows.Select(x => x.SalesEmployeeBP).Concat(openRows.Select(x => x.SalesEmployeeBP)).Concat(availableSalesEmployeesRaw), ct);

            string MapSalesEmployeeName(string? raw) =>
                !string.IsNullOrWhiteSpace(raw) && salesEmployeeNameMap.TryGetValue(raw, out var mapped) ? mapped : (raw ?? "");

            var availableSalesEmployees = availableSalesEmployeesRaw
                .Select(MapSalesEmployeeName)
                .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "Department", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            // แทนชื่อดิบจาก Zoho ด้วยชื่อเต็มในตารางที่คำนวณไปแล้ว (ไม่กระทบลำดับ/การนับ เพราะ group ด้วยชื่อดิบไปแล้วก่อนหน้านี้)
            foreach (var row in bySalesperson) row.GroupName = MapSalesEmployeeName(row.GroupName);
            foreach (var row in recentOpportunities) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);
            foreach (var row in openOpportunities) row.SalesEmployeeBP = MapSalesEmployeeName(row.SalesEmployeeBP);

            return new OpportunityWinRateDto
            {
                Kpi = new WinRateKpiDto
                {
                    WonOpportunities = won.Count,
                    LostOpportunities = lost.Count,
                    TotalClosedOpportunities = totalClosed,
                    WinRatePercent = winRate,
                    LossRatePercent = lossRate,
                    WonValue = wonValue,
                    LostValue = lostValue,
                    ValueWinRatePercent = valueWinRate,
                    AvgWonDealSize = avgWonDealSize,
                    AvgLostDealSize = avgLostDealSize,
                    AvgSalesCycleWonDays = avgCycleWon,
                    AvgSalesCycleLostDays = avgCycleLost,
                    WonVsPreviousCount = won.Count - prevWonCount,
                    ClosedVsPreviousCount = totalClosed - prevClosedCount,
                    WinRateVsPreviousPoint = (winRate.HasValue && prevWinRate.HasValue) ? Math.Round(winRate.Value - prevWinRate.Value, 1) : null,
                    ValueWinRateVsPreviousPoint = (valueWinRate.HasValue && prevValueWinRate.HasValue) ? Math.Round(valueWinRate.Value - prevValueWinRate.Value, 1) : null,
                    AvgWonDealSizeVsPreviousPercent = prevAvgWonDealSize == 0 ? null : Math.Round((avgWonDealSize - prevAvgWonDealSize) * 100m / prevAvgWonDealSize, 1)
                },
                Trend = trend,
                BySalesperson = bySalesperson,
                ByProduct = byProduct,
                ByIndustry = byIndustry,
                PipelineByStage = pipeline,
                RecentClosedOpportunities = recentOpportunities,
                OpenOpportunities = openOpportunities,
                Summary = summary,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableProducts = availableProducts,
                AvailableIndustries = availableIndustries
            };
        }

        private async Task<(int PrevWon, int PrevClosed, decimal? PrevWinRate, decimal? PrevValueWinRate, decimal PrevAvgWonDealSize)>
            GetPreviousPeriodStatsAsync(OpportunityWinRateFilter filter, Func<DateTime?, DateTime?, IQueryable<MGT_Deal>> closedInRange, CancellationToken ct)
        {
            if (!filter.DateFrom.HasValue || !filter.DateTo.HasValue)
                return (0, 0, null, null, 0);

            var length = filter.DateTo.Value - filter.DateFrom.Value;
            var prevTo = filter.DateFrom.Value.AddDays(-1);
            var prevFrom = prevTo - length;

            var prevRows = await closedInRange(prevFrom, prevTo)
                .Select(x => new { x.ForecastCategory, x.DealAmount })
                .ToListAsync(ct);

            var prevWon = prevRows.Count(x => IsWon(x.ForecastCategory));
            var prevClosed = prevRows.Count;
            var prevWonValue = prevRows.Where(x => IsWon(x.ForecastCategory)).Sum(x => x.DealAmount ?? 0);
            var prevLostValue = prevRows.Where(x => IsLost(x.ForecastCategory)).Sum(x => x.DealAmount ?? 0);

            decimal? prevWinRate = prevClosed == 0 ? null : Math.Round(prevWon * 100m / prevClosed, 1);
            decimal? prevValueWinRate = (prevWonValue + prevLostValue) == 0 ? null : Math.Round(prevWonValue * 100m / (prevWonValue + prevLostValue), 1);
            var prevAvgWonDealSize = prevWon == 0 ? 0 : Math.Round(prevWonValue / prevWon, 2);

            return (prevWon, prevClosed, prevWinRate, prevValueWinRate, prevAvgWonDealSize);
        }

        private async Task<List<WinRateTrendPointDto>> GetTrendAsync(
            OpportunityWinRateFilter filter, Func<DateTime?, DateTime?, IQueryable<MGT_Deal>> closedInRange, CancellationToken ct)
        {
            var trendEnd = filter.DateTo ?? DateTime.Now.Date;
            var trendStart = new DateTime(trendEnd.Year, trendEnd.Month, 1).AddMonths(-11);

            var trendRows = await closedInRange(trendStart, trendEnd)
                .Select(x => new { x.ForecastCategory, EffectiveClosedDate = x.ActualClosedDate ?? x.ClosingDate })
                .ToListAsync(ct);

            var trend = new List<WinRateTrendPointDto>();
            for (var i = 0; i < 12; i++)
            {
                var monthStart = trendStart.AddMonths(i);
                var monthRows = trendRows.Where(x => x.EffectiveClosedDate.HasValue
                    && x.EffectiveClosedDate.Value.Year == monthStart.Year
                    && x.EffectiveClosedDate.Value.Month == monthStart.Month).ToList();

                var wonM = monthRows.Count(x => IsWon(x.ForecastCategory));
                var lostM = monthRows.Count(x => IsLost(x.ForecastCategory));
                var closedM = monthRows.Count;

                trend.Add(new WinRateTrendPointDto
                {
                    PeriodLabel = monthStart.ToString("MMM yy"),
                    WonDeals = wonM,
                    LostDeals = lostM,
                    ClosedDeals = closedM,
                    WinRatePercent = closedM == 0 ? null : Math.Round(wonM * 100m / closedM, 1)
                });
            }
            // แสดงเฉพาะเดือนที่มีข้อมูลจริง (ตัดเดือนที่ไม่มี closed deal เลยออก ไม่ให้กราฟรกด้วยเดือนว่าง)
            return trend.Where(x => x.ClosedDeals > 0).ToList();
        }

        private async Task<List<WinRateGroupRowDto>> GetByProductAsync(OpportunityWinRateFilter filter, List<string>? lockedRawNames, CancellationToken ct)
        {
            var scopeQuery = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), filter, lockedRawNames)
                .Where(x => x.ForecastCategory == WonCategory || x.ForecastCategory == LostCategory);

            if (filter.DateFrom.HasValue) scopeQuery = scopeQuery.Where(x => (x.ActualClosedDate ?? x.ClosingDate) >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) scopeQuery = scopeQuery.Where(x => (x.ActualClosedDate ?? x.ClosingDate) <= filter.DateTo.Value);

            var rows = await (
                from p in _db.MGT_DealProduct.AsNoTracking()
                join d in scopeQuery on p.DealId equals d.DealId
                select new { p.Product, d.ForecastCategory }
            ).ToListAsync(ct);

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Product))
                .GroupBy(x => x.Product!)
                .Select(g => BuildGroupRow(g.Key, g.Select(x => x.ForecastCategory).ToList()))
                .OrderByDescending(x => x.ClosedDeals)
                .ToList();
        }

        private async Task<List<StageFunnelRowDto>> GetPipelineAsync(IQueryable<MGT_Deal> scopeQuery, CancellationToken ct)
        {
            var openRows = await scopeQuery
                .Where(x => x.Stage != null && x.ForecastCategory != WonCategory && x.ForecastCategory != LostCategory)
                .GroupBy(x => x.Stage)
                .Select(g => new { Stage = g.Key!, Count = g.Count() })
                .ToListAsync(ct);

            return openRows
                .Select(x => new StageFunnelRowDto { Stage = x.Stage, DealCount = x.Count })
                .OrderByDescending(x => x.DealCount)
                .ToList();
        }

        private static WinRateGroupRowDto BuildGroupRow(string name, List<string?> forecastCategories)
        {
            var won = forecastCategories.Count(IsWon);
            var lost = forecastCategories.Count(IsLost);
            var closed = won + lost;
            return new WinRateGroupRowDto
            {
                GroupName = name,
                WonDeals = won,
                LostDeals = lost,
                ClosedDeals = closed,
                WinRatePercent = closed == 0 ? null : Math.Round(won * 100m / closed, 1)
            };
        }

        private static bool IsWon(string? forecastCategory) => forecastCategory == WonCategory;
        private static bool IsLost(string? forecastCategory) => forecastCategory == LostCategory;

        // ★ จับคู่ชื่อดิบจาก Zoho (SalesEmployeeBP) กับชื่อเต็มใน Ms_User — ดู comment จุดที่เรียกใช้ด้านบนสำหรับข้อจำกัด
        private async Task<List<string>> GetDistinctDealFieldAsync(
            OpportunityWinRateFilter filter, Expression<Func<MGT_Deal, string?>> keySelector, List<string>? lockedRawNames, CancellationToken ct)
        {
            // dropdown scope ตาม SalesGroup เท่านั้น (ไม่ใส่ filter ของตัวเอง) เพื่อให้เห็นตัวเลือกครบหลังเลือกแล้ว
            var baseFilter = new OpportunityWinRateFilter { SalesGroup = filter.SalesGroup };
            var query = ApplyScopeFilter(_db.MGT_Deal.AsNoTracking(), baseFilter, lockedRawNames);

            return await query
                .Select(keySelector)
                .Where(v => v != null && v != "")
                .Select(v => v!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync(ct);
        }

        private async Task<List<string>> GetAvailableProductsAsync(List<string>? lockedRawNames, CancellationToken ct)
        {
            var dealQuery = _db.MGT_Deal.AsNoTracking().AsQueryable();
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
        // ของธุรกรรมเอง — ดู ISalesEmployeeNameResolver.GetActiveSalesEmployeeRawDealNamesAsync
        private IQueryable<MGT_Deal> ApplyScopeFilter(IQueryable<MGT_Deal> query, OpportunityWinRateFilter filter, List<string>? lockedRawNames)
        {
            if (lockedRawNames is not null)
                query = query.Where(x => x.SalesEmployeeBP != null && lockedRawNames.Contains(x.SalesEmployeeBP));

            // ★ filter.SalesEmployeeBP อาจเป็นชื่อเต็ม (จากดรอปดาวน์ที่ map แล้ว หรือ FullName ที่ ResolveEffectiveSalesEmployeeFilter
            // ทับให้ตอน Position=Sales) ขณะที่ x.SalesEmployeeBP เป็นชื่อดิบบางส่วนจาก Zoho — จึงเทียบแบบ "ชื่อที่ส่งมา
            // contains ชื่อดิบใน DB" แทนการเทียบเท่ากันตรงๆ (ยังคง match ปกติถ้าเป็นชื่อดิบเดียวกันเป๊ะ เพราะ string contains ตัวเอง)
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP != null && filter.SalesEmployeeBP.Contains(x.SalesEmployeeBP));

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            // ★ cross-filter จากการเลือก Product — Deal ต้องมี Product นี้อยู่ในรายการสินค้าของตัวเอง (subquery ตาราง MGT_DealProduct)
            if (!string.IsNullOrWhiteSpace(filter.Product))
                query = query.Where(x => _db.MGT_DealProduct.Any(p => p.DealId == x.DealId && p.Product == filter.Product));

            return query;
        }
    }
}
