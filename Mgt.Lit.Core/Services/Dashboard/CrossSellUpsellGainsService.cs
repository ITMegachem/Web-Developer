using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Data;

namespace Mgt.Lit.Core.Services.Dashboard
{
    // Cross-Sell & Upsell Gains Report — ใช้ MGT_Sale (ข้อมูลจาก SAP Billing Document ที่ sync ไว้แล้ว)
    // เป็น Source of Truth สำหรับ Revenue ตามที่สเปคแนะนำ (ไม่ใช้ Zoho Deals เพราะนั่นคือ pipeline ไม่ใช่รายได้จริง)
    //
    // แนวคิดหลัก (กัน double-count ตามสเปคข้อ 24):
    // - Existing Customer  = FirstPurchaseDate < DateFrom (คำนวณจากประวัติทั้งหมด ไม่จำกัดปี)
    // - Historical Product Groups(customer) = กลุ่มสินค้าที่เคยซื้อ "ก่อน" DateFrom (ไม่ว่าจะกี่ปีที่แล้ว)
    // - แต่ละแถว (Customer × ProductGroup) ในช่วง Current Period จัดเป็นอย่างใดอย่างหนึ่งเท่านั้น:
    //     Product Group ไม่เคยซื้อมาก่อน DateFrom  -> Cross-Sell (รายได้ทั้งก้อนของกลุ่มนั้น)
    //     Product Group เคยซื้อมาก่อน DateFrom      -> Upsell Gain = MAX(CurrentRevenue - BaselineRevenue, 0)
    //       (Baseline = รายได้ของคู่ Customer+ProductGroup นั้น ในช่วงเวลาเดียวกันของปีก่อนหน้า — ตรงกับที่ mockup
    //        เทียบ "vs Jan-May 2023" ไม่ใช่ช่วงก่อนหน้าติดกัน)
    //
    // ★★★ Performance tuning (2026-09) ★★★
    // MGT_Sale เป็น VIEW ที่ไม่มี index ได้ (join ผ่าน base table อีกหลายชั้น รวมถึง Op_SalesOrder ที่ report อื่นก็ใช้อยู่
    // จึงตัดสินใจไม่แตะ/ไม่ทำ index ที่ต้นทาง — ดู comment ใน AverageDaysToCloseService ประกอบ) ทุก query ที่ยิงไปที่ view นี้
    // จึงมีต้นทุนคงที่สูง (evaluate join chain ทั้งชุด) โดยไม่ขึ้นกับว่าฝั่ง SQL group/filter ให้แล้วหรือไม่
    // เดิม service นี้ยิง query แยกไปที่ MGT_Sale ถึง ~17 ครั้งต่อการโหลด 1 ครั้ง (firstPurchase/historicalGroups ซ้ำ 3 รอบ,
    // revenue-by-period ซ้ำ 6 รอบ, industry map, dropdown อีก 4 คอลัมน์แยกกัน) ทั้งที่ข้อมูลส่วนใหญ่ซ้ำกันข้าม request
    // ย่อยเดียวกัน — ปรับให้เหลือ 3 query หลัก แล้ว group/slice ต่อในหน่วยความจำแทน:
    //   1) GetAllTimeGroupFirstPurchaseAsync — all-time MIN(date) ต่อ (Customer, ProductGroup) + Industry
    //      ใช้ทำทั้ง existingCustomers, historicalByCustomer (ที่ cutoff ต่างกัน 2 จุด) และ industryByCustomer ในครั้งเดียว
    //   2) GetWindowedRevenueAsync — ยอดขายต่อ (Customer, ProductGroup, วันที่จริง) ในช่วง [DateFrom-2ปี, DateTo]
    //      ครอบคลุมทั้ง 3 หน้าต่างเวลาที่ต้องใช้ (ปีปัจจุบัน/ปีก่อน/ปีก่อนนั้นอีกที) รวมถึง trend รายเดือน — slice ต่อในหน่วยความจำ
    //      (เก็บวันที่จริงไว้ ไม่ group รวมเป็นเดือนตั้งแต่ SQL เพราะ DateFrom/DateTo อาจไม่ตรงต้นเดือน ต้องกรองระดับวันให้ตรงกับของเดิม)
    //   3) GetDropdownsAsync — รวม 4 คอลัมน์ dropdown เป็น query เดียว (distinct ที่ระดับ tuple แล้วแยกแต่ละคอลัมน์ในหน่วยความจำ)
    public class CrossSellUpsellGainsService : ICrossSellUpsellGainsService
    {
        private const int MaxTrendMonths = 36;

        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public CrossSellUpsellGainsService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private sealed record GroupClassificationRow(string Customer, string Group, decimal Revenue, decimal BaselineRevenue, bool IsCrossSell);

        private sealed record CustomerExpansionRow(
            string Customer, decimal ExistingRevenue, decimal CrossSellRevenue, decimal UpsellRevenue, int GroupCount, List<string> Groups);

        private sealed record MonthlyGroupRow(string Customer, string Group, int Year, int Month, decimal Revenue);

        private sealed record ExpansionSummary(
            decimal ExistingRevenue, decimal CrossSellRevenue, decimal UpsellRevenue,
            int ActiveCustomers, int ExistingCustomersCount, int CrossSellCustomers, int UpsellCustomers,
            double AvgGroupsPerCustomer, int Buy1GroupCustomers);

        // (Customer, ProductGroup) แรกที่เคยซื้อ (all-time) + Industry ล่าสุดที่เจอของ Customer นั้น
        private sealed record AllTimeGroupRow(string Customer, string Group, DateTime FirstPurchase, string? Industry);

        // ยอดขายต่อ (Customer, ProductGroup, วันที่จริง) — เก็บระดับวันไว้เพื่อ slice ช่วงเวลาต่างๆ ในหน่วยความจำได้ตรงกับของเดิมเป๊ะ
        private sealed record WindowedSaleRow(string Customer, string Group, DateTime Date, decimal Revenue);

        public async Task<CrossSellUpsellGainsDto> GetAsync(CrossSellUpsellGainsFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var dateFrom = filter.DateFrom ?? new DateTime(DateTime.Now.Year, 1, 1);
            var dateTo = filter.DateTo ?? DateTime.Now.Date;

            // ★ นิยาม BU ใหม่ทั้งรายงาน — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.SalesGroup, ct);

            var scopeQuery = ApplyScopeFilter(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            var dropdownScopeQuery = ApplyScopeFilter(_db.MGT_Sale.AsNoTracking(), new CrossSellUpsellGainsFilter { SalesGroup = filter.SalesGroup }, lockedBuNames);

            // ── 3 query หลักที่ยิงไปที่ MGT_Sale จริงๆ (ดู comment บน class) ─────────────────────
            var allTimeGroups = await GetAllTimeGroupFirstPurchaseAsync(scopeQuery, ct);
            var windowedRevenue = await GetWindowedRevenueAsync(scopeQuery, dateFrom.AddYears(-2), dateTo, ct);
            var (availableSalesEmployees, availableProductGroups, availableIndustries, availableCustomerGroups) =
                await GetDropdownsAsync(dropdownScopeQuery, filter.SalesGroup, ct);

            // ── สร้างชุดข้อมูล all-time ที่ cutoff ต่างกัน (ใช้ allTimeGroups ชุดเดียวกันทุกจุด ไม่ยิง query ซ้ำ) ──
            var existingCustomersY0 = BuildExistingCustomers(allTimeGroups, dateFrom);
            var existingCustomersYm1 = BuildExistingCustomers(allTimeGroups, dateFrom.AddYears(-1));
            var historicalY0 = BuildHistoricalByCustomer(allTimeGroups, dateFrom);
            var historicalYm1 = BuildHistoricalByCustomer(allTimeGroups, dateFrom.AddYears(-1));
            var industryByCustomer = BuildIndustryMap(allTimeGroups);

            // ── slice ยอดขายทั้ง 3 หน้าต่างเวลาจาก windowedRevenue ชุดเดียว (แทนการยิง query ละหน้าต่าง) ──
            var revenueY0 = RevenueForPeriod(windowedRevenue, dateFrom, dateTo);
            var revenueYm1 = RevenueForPeriod(windowedRevenue, dateFrom.AddYears(-1), dateTo.AddYears(-1));
            var revenueYm2 = RevenueForPeriod(windowedRevenue, dateFrom.AddYears(-2), dateTo.AddYears(-2));

            var (rows, existingCount) = ClassifyGroups(revenueY0, revenueYm1, existingCustomersY0, historicalY0);
            var (prevRows, prevExistingCount) = ClassifyGroups(revenueYm1, revenueYm2, existingCustomersYm1, historicalYm1);

            var perCustomer = BuildPerCustomer(rows);
            var prevPerCustomer = BuildPerCustomer(prevRows);

            var summary = Summarize(perCustomer, existingCount);
            var prevSummary = Summarize(prevPerCustomer, prevExistingCount);
            var kpi = BuildKpi(summary, prevSummary);

            var trend = BuildTrend(windowedRevenue, existingCustomersY0, historicalY0, dateFrom, dateTo);
            var customerGrowth = BuildCustomerGrowth(perCustomer, prevPerCustomer);

            var prevByCustomer = prevPerCustomer.ToDictionary(x => x.Customer);
            var topCustomerNames = perCustomer
                .OrderByDescending(x => x.ExistingRevenue)
                .Take(10)
                .Select(x => x.Customer)
                .ToList();
            var latestSalesEmployeeByCustomer = await GetLatestSalesEmployeeByCustomerAsync(scopeQuery, topCustomerNames, ct);

            var topCustomers = perCustomer
                .OrderByDescending(x => x.ExistingRevenue)
                .Take(10)
                .Select(x =>
                {
                    decimal? yoy = prevByCustomer.TryGetValue(x.Customer, out var prev) && prev.ExistingRevenue != 0
                        ? Math.Round((x.ExistingRevenue - prev.ExistingRevenue) * 100m / prev.ExistingRevenue, 1)
                        : null;
                    return new TopCustomerExpansionRowDto
                    {
                        CustomerName = x.Customer,
                        SalesEmployeeBP = latestSalesEmployeeByCustomer.TryGetValue(x.Customer, out var sp) ? sp : "",
                        ExistingRevenue = x.ExistingRevenue,
                        CrossSellRevenue = x.CrossSellRevenue,
                        UpsellRevenue = x.UpsellRevenue,
                        ExpansionRevenue = x.CrossSellRevenue + x.UpsellRevenue,
                        YoYGrowthPercent = yoy
                    };
                })
                .ToList();

            var byGroupCount = BuildGroupCountBuckets(perCustomer);

            var crossSellGroups = rows.Where(r => r.IsCrossSell)
                .GroupBy(r => r.Group)
                .Select(g => new { Group = g.Key, Revenue = g.Sum(x => x.Revenue), Customers = g.Select(x => x.Customer).Distinct().Count() })
                .OrderByDescending(x => x.Revenue)
                .ToList();
            var crossSellTotal = crossSellGroups.Sum(x => x.Revenue);
            var crossSellByProductGroup = crossSellGroups.Select((x, i) => new CrossSellByProductGroupRowDto
            {
                Rank = i + 1,
                ProductGroup = x.Group,
                CrossSellRevenue = x.Revenue,
                CrossSellCustomers = x.Customers,
                SharePercent = crossSellTotal == 0 ? 0 : Math.Round(x.Revenue * 100m / crossSellTotal, 1),
                Customers = rows.Where(r => r.IsCrossSell && r.Group == x.Group)
                    .OrderByDescending(r => r.Revenue)
                    .Select(r => new ProductGroupRevenueCustomerDto { CustomerName = r.Customer, Revenue = r.Revenue })
                    .ToList()
            }).ToList();

            var upsellByProductGroup = rows.Where(r => !r.IsCrossSell)
                .GroupBy(r => r.Group)
                .Select(g =>
                {
                    var prevRev = g.Sum(x => x.BaselineRevenue);
                    var curRev = g.Sum(x => x.Revenue);
                    return new UpsellByProductGroupRowDto
                    {
                        ProductGroup = g.Key,
                        PreviousRevenue = prevRev,
                        CurrentRevenue = curRev,
                        GainRevenue = g.Sum(x => Math.Max(x.Revenue - x.BaselineRevenue, 0)),
                        GrowthPercent = prevRev == 0 ? null : Math.Round((curRev - prevRev) * 100m / prevRev, 1),
                        UpsellCustomers = g.Count(x => x.Revenue > x.BaselineRevenue)
                    };
                })
                .OrderByDescending(x => x.GainRevenue)
                .ToList();

            var crossSellOpportunities = BuildCrossSellOpportunities(perCustomer, rows, industryByCustomer);

            return new CrossSellUpsellGainsDto
            {
                Kpi = kpi,
                Trend = trend,
                CustomerGrowthYoY = customerGrowth,
                TopCustomers = topCustomers,
                ByProductGroupCount = byGroupCount,
                CrossSellByProductGroup = crossSellByProductGroup,
                UpsellByProductGroup = upsellByProductGroup,
                CrossSellOpportunities = crossSellOpportunities,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableProductGroups = availableProductGroups,
                AvailableIndustries = availableIndustries,
                AvailableCustomerGroups = availableCustomerGroups
            };
        }

        // ── 1 ใน 3 query หลัก: all-time MIN(date) ต่อ (Customer, ProductGroup) + Industry ─────────────
        // ใช้แทนของเดิม 3 จุด: firstPurchase-per-customer (x3), historicalRaw distinct pairs (x3), industry map (x1)
        private static async Task<List<AllTimeGroupRow>> GetAllTimeGroupFirstPurchaseAsync(IQueryable<MGT_Sale> scopeQuery, CancellationToken ct)
        {
            var rows = await scopeQuery
                .Where(x => x.CustomerFullName != null && x.CustomerFullName != "" && x.MaterialGroupName != null && x.BillingDocumentDate.HasValue)
                .GroupBy(x => new { x.CustomerFullName, x.MaterialGroupName })
                .Select(g => new
                {
                    Customer = g.Key.CustomerFullName!,
                    Group = g.Key.MaterialGroupName!,
                    FirstPurchase = g.Min(x => x.BillingDocumentDate!.Value),
                    Industry = g.Max(x => x.IndustryName)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AllTimeGroupRow(r.Customer, r.Group, r.FirstPurchase, r.Industry)).ToList();
        }

        // ── 2 ใน 3 query หลัก: ยอดขายต่อ (Customer, ProductGroup, วันที่จริง) ในช่วงกว้างพอสำหรับทุกหน้าต่างเวลาที่ต้องใช้ ──
        private static async Task<List<WindowedSaleRow>> GetWindowedRevenueAsync(
            IQueryable<MGT_Sale> scopeQuery, DateTime from, DateTime to, CancellationToken ct)
        {
            var rows = await scopeQuery
                .Where(x => x.BillingDocumentDate.HasValue && x.BillingDocumentDate >= from && x.BillingDocumentDate <= to)
                .Where(x => x.CustomerFullName != null && x.MaterialGroupName != null)
                .GroupBy(x => new { x.CustomerFullName, x.MaterialGroupName, x.BillingDocumentDate })
                .Select(g => new { g.Key.CustomerFullName, g.Key.MaterialGroupName, Date = g.Key.BillingDocumentDate!.Value, Revenue = g.Sum(x => x.NetAmount ?? 0) })
                .ToListAsync(ct);

            return rows.Select(r => new WindowedSaleRow(r.CustomerFullName!, r.MaterialGroupName!, r.Date, r.Revenue)).ToList();
        }

        // ── 3 ใน 3 query หลัก: dropdown ทั้ง 4 คอลัมน์ในครั้งเดียว (distinct ระดับ tuple แล้วแยกในหน่วยความจำ) ──
        private async Task<(List<string> SalesEmployees, List<string> ProductGroups, List<string> Industries, List<string> CustomerGroups)>
            GetDropdownsAsync(IQueryable<MGT_Sale> dropdownScopeQuery, string? salesGroup, CancellationToken ct)
        {
            var raw = await dropdownScopeQuery
                .Select(x => new { x.SalesEmployeeBP, x.MaterialGroupName, x.IndustryName, x.AffiliateCustomerName })
                .Distinct()
                .ToListAsync(ct);

            static List<string> DistinctSorted(IEnumerable<string?> values) =>
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

            // ★ กรองซ้ำด้วย Division ประจำของพนักงาน (Ms_User.Division) เมื่อ BU ถูกล็อก — MGT_Sale.SalesEmployeeBP
            // อิงจาก SalesGroup ของ "ธุรกรรม" ไม่ใช่ของ "คน" จึงมีบางแถวหลุดข้าม BU ของพนักงานคนนั้นได้ (ดู comment ที่
            // ISalesEmployeeNameResolver.FilterToDivisionAsync)
            var salesEmployees = await _nameResolver.FilterToDivisionAsync(DistinctSorted(raw.Select(x => x.SalesEmployeeBP)), salesGroup, ct);

            return (
                salesEmployees,
                DistinctSorted(raw.Select(x => x.MaterialGroupName)),
                DistinctSorted(raw.Select(x => x.IndustryName)),
                DistinctSorted(raw.Select(x => x.AffiliateCustomerName))
            );
        }

        // ★ ดึงชื่อ salesperson จากรายการขายล่าสุด (BillingDocumentDate มากสุด) ของแต่ละ customer — ใช้เฉพาะ Top 10
        // ที่จะแสดงผลจริงเท่านั้น (ไม่ query ทั้งตาราง) ตาม pattern เดียวกับ SalesForecastAccuracyService.GetForecastVsActualAsync
        // (ดึงมาก่อนแล้ว GroupBy/OrderByDescending ในหน่วยความจำ ไม่ทำเป็น SQL window function)
        private static async Task<Dictionary<string, string>> GetLatestSalesEmployeeByCustomerAsync(
            IQueryable<MGT_Sale> scopeQuery, List<string> customerNames, CancellationToken ct)
        {
            if (customerNames.Count == 0) return new();

            var rows = await scopeQuery
                .Where(x => x.CustomerFullName != null && customerNames.Contains(x.CustomerFullName) && x.BillingDocumentDate.HasValue)
                .Select(x => new { x.CustomerFullName, x.BillingDocumentDate, x.SalesEmployeeBP })
                .ToListAsync(ct);

            return rows
                .GroupBy(x => x.CustomerFullName!)
                .Select(g => g.OrderByDescending(x => x.BillingDocumentDate).First())
                .Where(x => !string.IsNullOrWhiteSpace(x.SalesEmployeeBP))
                .ToDictionary(x => x.CustomerFullName!, x => x.SalesEmployeeBP!);
        }

        private static HashSet<string> BuildExistingCustomers(List<AllTimeGroupRow> allTimeGroups, DateTime periodFrom) =>
            allTimeGroups
                .GroupBy(x => x.Customer)
                .Where(g => g.Min(x => x.FirstPurchase).Date < periodFrom.Date)
                .Select(g => g.Key)
                .ToHashSet();

        private static Dictionary<string, HashSet<string>> BuildHistoricalByCustomer(List<AllTimeGroupRow> allTimeGroups, DateTime periodFrom) =>
            allTimeGroups
                .Where(x => x.FirstPurchase < periodFrom)
                .GroupBy(x => x.Customer)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Group).ToHashSet());

        private static Dictionary<string, string> BuildIndustryMap(List<AllTimeGroupRow> allTimeGroups) =>
            allTimeGroups
                .Where(x => !string.IsNullOrWhiteSpace(x.Industry))
                .GroupBy(x => x.Customer)
                .ToDictionary(g => g.Key, g => g.First(x => !string.IsNullOrWhiteSpace(x.Industry)).Industry!);

        private static List<(string Customer, string Group, decimal Revenue)> RevenueForPeriod(List<WindowedSaleRow> windowed, DateTime from, DateTime to) =>
            windowed
                .Where(r => r.Date >= from && r.Date <= to)
                .GroupBy(r => (r.Customer, r.Group))
                .Select(g => (g.Key.Customer, g.Key.Group, g.Sum(x => x.Revenue)))
                .ToList();

        private static List<MonthlyGroupRow> MonthlyForPeriod(List<WindowedSaleRow> windowed, DateTime from, DateTime to) =>
            windowed
                .Where(r => r.Date >= from && r.Date <= to)
                .GroupBy(r => (r.Customer, r.Group, r.Date.Year, r.Date.Month))
                .Select(g => new MonthlyGroupRow(g.Key.Customer, g.Key.Group, g.Key.Year, g.Key.Month, g.Sum(x => x.Revenue)))
                .ToList();

        // ── การจัดกลุ่ม Cross-Sell/Upsell ระดับ (Customer x ProductGroup) ทั้งช่วงเวลา (period-level ตามตัวอย่างในสเปคข้อ 9) ──
        // (ล้วนเป็นข้อมูลที่ดึงมาแล้ว ไม่มีการยิง query เพิ่ม — เดิมเป็น ComputeClassificationAsync ที่ยิง query เองด้วย)
        private static (List<GroupClassificationRow> Rows, int ExistingCustomersCount) ClassifyGroups(
            List<(string Customer, string Group, decimal Revenue)> currentRevenue,
            List<(string Customer, string Group, decimal Revenue)> baselineRevenue,
            HashSet<string> existingCustomers,
            Dictionary<string, HashSet<string>> historicalByCustomer)
        {
            var baselineLookup = baselineRevenue.ToDictionary(r => (r.Customer, r.Group), r => r.Revenue);

            var rows = currentRevenue
                .Where(r => existingCustomers.Contains(r.Customer))
                .Select(r =>
                {
                    var histGroups = historicalByCustomer.TryGetValue(r.Customer, out var hg) ? hg : new HashSet<string>();
                    var isCrossSell = !histGroups.Contains(r.Group);
                    var baseline = isCrossSell ? 0 : (baselineLookup.TryGetValue((r.Customer, r.Group), out var b) ? b : 0);
                    return new GroupClassificationRow(r.Customer, r.Group, r.Revenue, baseline, isCrossSell);
                })
                .ToList();

            return (rows, existingCustomers.Count);
        }

        private static List<CustomerExpansionRow> BuildPerCustomer(List<GroupClassificationRow> rows) =>
            rows.GroupBy(r => r.Customer).Select(g => new CustomerExpansionRow(
                g.Key,
                g.Sum(x => x.Revenue),
                g.Where(x => x.IsCrossSell).Sum(x => x.Revenue),
                g.Where(x => !x.IsCrossSell).Sum(x => Math.Max(x.Revenue - x.BaselineRevenue, 0)),
                g.Select(x => x.Group).Distinct().Count(),
                g.Select(x => x.Group).Distinct().ToList()
            )).ToList();

        private static ExpansionSummary Summarize(List<CustomerExpansionRow> perCustomer, int existingCustomersCount)
        {
            var active = perCustomer.Count;
            return new ExpansionSummary(
                perCustomer.Sum(x => x.ExistingRevenue),
                perCustomer.Sum(x => x.CrossSellRevenue),
                perCustomer.Sum(x => x.UpsellRevenue),
                active,
                existingCustomersCount,
                perCustomer.Count(x => x.CrossSellRevenue > 0),
                perCustomer.Count(x => x.UpsellRevenue > 0),
                active == 0 ? 0 : perCustomer.Average(x => x.GroupCount),
                perCustomer.Count(x => x.GroupCount == 1));
        }

        private static CrossSellUpsellKpiDto BuildKpi(ExpansionSummary s, ExpansionSummary prev)
        {
            static decimal? PctChange(decimal cy, decimal py) => py == 0 ? null : Math.Round((cy - py) * 100m / py, 1);

            var expansion = s.CrossSellRevenue + s.UpsellRevenue;
            var prevExpansion = prev.CrossSellRevenue + prev.UpsellRevenue;

            var avgRevPerCustomer = s.ActiveCustomers == 0 ? 0 : s.ExistingRevenue / s.ActiveCustomers;
            var prevAvgRevPerCustomer = prev.ActiveCustomers == 0 ? 0 : prev.ExistingRevenue / prev.ActiveCustomers;

            var expansionRate = s.ExistingRevenue == 0 ? 0 : Math.Round(expansion * 100m / s.ExistingRevenue, 1);
            var prevExpansionRate = prev.ExistingRevenue == 0 ? 0 : Math.Round(prevExpansion * 100m / prev.ExistingRevenue, 1);

            var retentionRate = s.ExistingCustomersCount == 0 ? 0 : Math.Round(s.ActiveCustomers * 100m / s.ExistingCustomersCount, 1);
            var prevRetentionRate = prev.ExistingCustomersCount == 0 ? 0 : Math.Round(prev.ActiveCustomers * 100m / prev.ExistingCustomersCount, 1);

            return new CrossSellUpsellKpiDto
            {
                ExistingCustomerRevenue = s.ExistingRevenue,
                ExistingCustomerRevenueYoYPercent = PctChange(s.ExistingRevenue, prev.ExistingRevenue),
                CrossSellRevenue = s.CrossSellRevenue,
                CrossSellRevenueYoYPercent = PctChange(s.CrossSellRevenue, prev.CrossSellRevenue),
                UpsellRevenue = s.UpsellRevenue,
                UpsellRevenueYoYPercent = PctChange(s.UpsellRevenue, prev.UpsellRevenue),
                ExpansionRevenue = expansion,
                ExpansionRevenueYoYPercent = PctChange(expansion, prevExpansion),

                ActiveCustomers = s.ActiveCustomers,
                Buy1ProductGroupCustomers = s.Buy1GroupCustomers,

                ExistingCustomers = s.ExistingCustomersCount,
                CrossSellCustomers = s.CrossSellCustomers,
                CrossSellRatePercent = s.ExistingCustomersCount == 0 ? 0 : Math.Round(s.CrossSellCustomers * 100m / s.ExistingCustomersCount, 1),
                CrossSellCustomersYoYChange = s.CrossSellCustomers - prev.CrossSellCustomers,

                UpsellCustomers = s.UpsellCustomers,
                UpsellRatePercent = s.ExistingCustomersCount == 0 ? 0 : Math.Round(s.UpsellCustomers * 100m / s.ExistingCustomersCount, 1),
                UpsellCustomersYoYChange = s.UpsellCustomers - prev.UpsellCustomers,

                AvgProductGroupsPerCustomer = Math.Round(s.AvgGroupsPerCustomer, 1),
                AvgProductGroupsPerCustomerYoYChange = Math.Round(s.AvgGroupsPerCustomer - prev.AvgGroupsPerCustomer, 1),

                AvgRevenuePerCustomer = avgRevPerCustomer,
                AvgRevenuePerCustomerYoYPercent = PctChange(avgRevPerCustomer, prevAvgRevPerCustomer),

                ExpansionRevenueRatePercent = expansionRate,
                ExpansionRevenueRateYoYPoint = Math.Round(expansionRate - prevExpansionRate, 1),

                CustomerRetentionRatePercent = retentionRate,
                CustomerRetentionRateYoYPoint = Math.Round(retentionRate - prevRetentionRate, 1)
            };
        }

        private static List<CustomerGrowthRowDto> BuildCustomerGrowth(List<CustomerExpansionRow> current, List<CustomerExpansionRow> previous)
        {
            var prevLookup = previous.ToDictionary(x => x.Customer, x => x.ExistingRevenue);
            var curLookup = current.ToDictionary(x => x.Customer, x => x.ExistingRevenue);
            var allCustomers = curLookup.Keys.Union(prevLookup.Keys).ToList();

            return allCustomers
                .Select(c =>
                {
                    var cur = curLookup.TryGetValue(c, out var cv) ? cv : 0;
                    var prev = prevLookup.TryGetValue(c, out var pv) ? pv : 0;
                    decimal? growth = prev == 0 ? null : Math.Round((cur - prev) * 100m / prev, 1);
                    return new CustomerGrowthRowDto { CustomerName = c, CurrentRevenue = cur, PreviousRevenue = prev, GrowthPercent = growth };
                })
                // ตัดเคส PY=0 ออก (สเปคข้อ 19: ห้ามหารด้วย 0 หรือแสดง Growth เป็น 100% ให้เข้าใจผิด)
                .Where(x => x.GrowthPercent.HasValue)
                .OrderByDescending(x => x.GrowthPercent)
                .ToList();
        }

        private static List<ProductGroupCountRowDto> BuildGroupCountBuckets(List<CustomerExpansionRow> perCustomer)
        {
            var total = perCustomer.Count;
            var buckets = new (string Label, string Priority, Func<int, bool> Match)[]
            {
                ("1 Group", "High", n => n == 1),
                ("2 Groups", "Medium", n => n == 2),
                ("3 Groups", "Medium", n => n == 3),
                ("4+ Groups", "Develop", n => n >= 4)
            };

            return buckets.Select(b =>
            {
                var matched = perCustomer.Where(x => b.Match(x.GroupCount)).ToList();
                return new ProductGroupCountRowDto
                {
                    GroupCountLabel = b.Label,
                    CustomerCount = matched.Count,
                    SharePercent = total == 0 ? 0 : Math.Round(matched.Count * 100m / total, 1),
                    Priority = b.Priority,
                    Customers = matched
                        .OrderByDescending(x => x.ExistingRevenue)
                        .Select(x => new ProductGroupCountCustomerDto
                        {
                            CustomerName = x.Customer,
                            GroupCount = x.GroupCount,
                            Revenue = x.ExistingRevenue,
                            ProductGroups = x.Groups
                        })
                        .ToList()
                };
            }).ToList();
        }

        // ⚠️ ไม่ได้เช็ค Product Eligibility (ยังไม่มีข้อมูลว่าลูกค้า/อุตสาหกรรมไหนซื้อกลุ่มสินค้าใดได้บ้าง)
        // เป็นแค่ "รายชื่อผู้สมัครเบื้องต้น" (ลูกค้าที่ Penetration ต่ำ + สินค้าที่บริษัทขายแต่ลูกค้ายังไม่เคยซื้อ)
        // ให้ Sales นำไปกลั่นกรองต่อ ไม่ใช่ Opportunity ที่ยืนยันแล้ว — ตรงตามคำเตือนในสเปคข้อ 16
        private static List<CrossSellOpportunityRowDto> BuildCrossSellOpportunities(
            List<CustomerExpansionRow> perCustomer, List<GroupClassificationRow> rows, Dictionary<string, string> industryByCustomer)
        {
            var allGroupsSoldCompanyWide = rows.Select(r => r.Group).Distinct().ToList();

            return perCustomer
                .Where(x => x.GroupCount <= 2)
                .OrderBy(x => x.GroupCount)
                .ThenByDescending(x => x.ExistingRevenue)
                .Take(15)
                .Select(x => new CrossSellOpportunityRowDto
                {
                    CustomerName = x.Customer,
                    IndustryName = industryByCustomer.TryGetValue(x.Customer, out var ind) ? ind : "",
                    CurrentProductGroups = x.Groups,
                    UnpurchasedProductGroups = allGroupsSoldCompanyWide.Except(x.Groups).ToList(),
                    CurrentRevenue = x.ExistingRevenue
                })
                .ToList();
        }

        // ── Trend รายเดือน — ใช้ existingCustomers/historicalByCustomer ของ "ทั้งช่วง" ตัวเดียวกันทุกเดือน (cutoff = dateFrom
        //   เหมือนกับที่ใช้จัดกลุ่ม current period ด้านบน — คือ set เดียวกันเป๊ะ ไม่ต้องคำนวณใหม่)
        //   (ไม่คำนวณ cutoff ใหม่ทุกเดือน กัน Product Group ที่เพิ่งซื้อครั้งแรกในเดือนแรกของช่วง กลายเป็น "Existing" ในเดือนถัดไป)
        //   Upsell รายเดือนเทียบกับเดือนเดียวกันของปีก่อน — ผลรวมรายเดือนอาจไม่เท่ากับยอดรวมทั้งช่วงเป๊ะๆ เพราะ MAX(...,0)
        //   คิดแยกรายเดือน ไม่ใช่คิดจากยอดรวมทั้งช่วงครั้งเดียว (เป็นเรื่องปกติของกราฟเทรนด์ ไม่ใช่ bug)
        private static List<ExpansionTrendPointDto> BuildTrend(
            List<WindowedSaleRow> windowedRevenue, HashSet<string> existingCustomers, Dictionary<string, HashSet<string>> historicalByCustomer,
            DateTime dateFrom, DateTime dateTo)
        {
            var currentMonthly = MonthlyForPeriod(windowedRevenue, dateFrom, dateTo);
            var baselineMonthly = MonthlyForPeriod(windowedRevenue, dateFrom.AddYears(-1), dateTo.AddYears(-1));
            var baselineByMonth = baselineMonthly
                .GroupBy(r => (r.Customer, r.Group, r.Month))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            var trend = new List<ExpansionTrendPointDto>();
            var cursor = new DateTime(dateFrom.Year, dateFrom.Month, 1);
            var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);
            var guard = 0;

            while (cursor <= endMonth && guard < MaxTrendMonths)
            {
                var monthRows = currentMonthly.Where(r => r.Year == cursor.Year && r.Month == cursor.Month && existingCustomers.Contains(r.Customer));

                decimal baseRev = 0, crossSell = 0, upsell = 0;
                foreach (var row in monthRows)
                {
                    var hist = historicalByCustomer.TryGetValue(row.Customer, out var hg) ? hg : new HashSet<string>();
                    if (!hist.Contains(row.Group))
                    {
                        crossSell += row.Revenue;
                    }
                    else
                    {
                        var baseline = baselineByMonth.TryGetValue((row.Customer, row.Group, row.Month), out var b) ? b : 0;
                        var gain = Math.Max(row.Revenue - baseline, 0);
                        upsell += gain;
                        baseRev += row.Revenue - gain;
                    }
                }

                var totalExisting = baseRev + crossSell + upsell;
                trend.Add(new ExpansionTrendPointDto
                {
                    PeriodLabel = cursor.ToString("MMM yy"),
                    BaseRevenue = Math.Round(baseRev, 0),
                    CrossSellRevenue = Math.Round(crossSell, 0),
                    UpsellRevenue = Math.Round(upsell, 0),
                    ExpansionRevenueRatePercent = totalExisting == 0 ? 0 : Math.Round((crossSell + upsell) * 100m / totalExisting, 1)
                });

                cursor = cursor.AddMonths(1);
                guard++;
            }

            return trend;
        }

        private static IQueryable<MGT_Sale> ApplyScopeFilter(IQueryable<MGT_Sale> query, CrossSellUpsellGainsFilter filter, HashSet<string>? lockedBuNames)
        {
            if (lockedBuNames is not null)
                query = query.Where(x => x.SalesEmployeeBP != null && lockedBuNames.Contains(x.SalesEmployeeBP));

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            if (!string.IsNullOrWhiteSpace(filter.CustomerGroup))
                query = query.Where(x => x.AffiliateCustomerName == filter.CustomerGroup);

            // ⚠️ กรอง ProductGroup ที่ระดับ scope นี้เอง ทำให้การจัดกลุ่ม Cross-Sell/Upsell เห็นเฉพาะกลุ่มสินค้าที่เลือกเท่านั้น
            // (คือ trade-off ที่ยอมรับได้ — ตัวกรองนี้เหมาะกับ "ดูรายละเอียดกลุ่มสินค้านี้" มากกว่า "วิเคราะห์ cross-sell แบบเต็ม")
            if (!string.IsNullOrWhiteSpace(filter.ProductGroup))
                query = query.Where(x => x.MaterialGroupName == filter.ProductGroup);

            return query;
        }
    }
}
