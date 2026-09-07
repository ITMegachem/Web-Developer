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
    // ★★★ Performance tuning (2026-09) ★★★
    // MGT_Sale เป็น VIEW ที่ index ไม่ได้ (ดู comment เดียวกันใน CrossSellUpsellGainsService/PricingMarginPerformanceService)
    // เดิม service นี้ยิง query แยกไปที่ MGT_Sale ~9 ครั้ง โดยหลายครั้งเป็นการดึงข้อมูลของ "ลูกค้ากลุ่มเดียวกัน" ซ้ำ
    // ผ่าน .Contains(existingNames)/.Contains(lostNames) (WHERE IN รายชื่อลูกค้าเป็นพันชื่อ ซึ่งหนักทั้งฝั่ง query planning
    // และฝั่ง view evaluation) ทั้งที่ customerDates/existingRawRows/lostRawRows/existingRevenue/newRevenue/
    // previousExistingRevenue ล้วนเป็นการ slice ข้อมูลชุดเดียวกัน (ประวัติการซื้อทั้งหมดของลูกค้าใน scope) แค่คนละมุม
    // ปรับให้ดึง raw rows "ทั้งประวัติ" มาครั้งเดียว (34k แถวจริงตอน verify ≈ 360ms) แล้ว group/filter ทุกอย่างในหน่วยความจำ
    public class CustomerChurnAnalysisService : ICustomerChurnAnalysisService
    {
        private const int LostThresholdDays = 365;   // Existing customer นับเป็น Lost/Churn ตั้งแต่วันนี้ขึ้นไป
        private const int HighThresholdDays = 700;
        private const int CriticalThresholdDays = 1000;

        private readonly AppDbContext _db;

        public CustomerChurnAnalysisService(AppDbContext db) => _db = db;

        private sealed record ChurnRawRow(
            string Customer, DateTime Date, decimal NetAmount, decimal GrossProfit,
            string? IndustryName, string? SalesEmployeeBP, string? SoldToParty);

        public async Task<CustomerChurnAnalysisDto> GetAsync(CustomerChurnAnalysisFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var analysisYear = filter.Year ?? DateTime.Now.Year;
            var startOfPeriod = new DateTime(analysisYear, 1, 1);
            var endOfPeriod = new DateTime(analysisYear, 12, 31);
            var asOfDate = analysisYear == DateTime.Now.Year ? DateTime.Now.Date : endOfPeriod;
            var periodEnd = asOfDate < endOfPeriod ? asOfDate : endOfPeriod;

            var scopeQuery = ApplyScopeFilter(_db.MGT_Sale.AsNoTracking(), filter);
            var dropdownScopeQuery = ApplyScopeFilter(_db.MGT_Sale.AsNoTracking(), new CustomerChurnAnalysisFilter { SalesGroup = filter.SalesGroup });

            // ── query หลัก 2 ครั้งที่ยิงไปที่ MGT_Sale จริงๆ (raw ทั้งประวัติ + dropdown รวม) ──────────
            var raw = await GetRawRowsAsync(scopeQuery, ct);
            var (availableSalesEmployees, availableCustomerGroups, availableIndustries) = await GetDropdownsAsync(dropdownScopeQuery, ct);

            // ── 1) First/Last purchase ต่อลูกค้า (ทั้งประวัติ ภายใต้ scope ที่กรอง) — group ครั้งเดียว ใช้ซ้ำได้ทุกจุด ──
            var byCustomer = raw.GroupBy(x => x.Customer).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());
            var customerDates = byCustomer
                .Select(kv => new { Customer = kv.Key, FirstPurchase = kv.Value[0].Date, LastPurchase = kv.Value[^1].Date })
                .ToList();

            // Existing = ซื้อครั้งแรกก่อนต้นปีที่วิเคราะห์ / New = ซื้อครั้งแรกภายในปีที่วิเคราะห์
            var existing = customerDates.Where(c => c.FirstPurchase.Date < startOfPeriod).ToList();
            var newCust = customerDates.Where(c => c.FirstPurchase.Date >= startOfPeriod && c.FirstPurchase.Date <= endOfPeriod).ToList();

            var existingNames = existing.Select(c => c.Customer).ToHashSet();
            var newNames = newCust.Select(c => c.Customer).ToHashSet();
            var lost = existing.Where(c => (asOfDate.Date - c.LastPurchase.Date).TotalDays >= LostThresholdDays).ToList();
            var lostNames = lost.Select(c => c.Customer).ToHashSet();

            // ── 2) Representative industry ของ Existing customer (ใช้ทำ Churn by Industry) — จาก byCustomer ตัวเดียวกัน ──
            string RepresentativeIndustry(string customer) =>
                byCustomer.TryGetValue(customer, out var rows) && rows.Count > 0
                    ? (rows[^1].IndustryName ?? "Unknown")
                    : "Unknown";

            // ── 3) Revenue: Existing / New / Existing ปีก่อนหน้าช่วงเวลาเดียวกัน — slice จาก raw ในหน่วยความจำ ──
            var existingRevenue = existingNames.Count == 0 ? 0 : raw
                .Where(x => x.Date >= startOfPeriod && x.Date <= periodEnd && existingNames.Contains(x.Customer))
                .Sum(x => x.NetAmount);

            var newRevenue = newNames.Count == 0 ? 0 : raw
                .Where(x => x.Date >= startOfPeriod && x.Date <= periodEnd && newNames.Contains(x.Customer))
                .Sum(x => x.NetAmount);

            var prevStart = startOfPeriod.AddYears(-1);
            var prevEnd = periodEnd.AddYears(-1);
            var previousExistingRevenue = existingNames.Count == 0 ? 0 : raw
                .Where(x => x.Date >= prevStart && x.Date <= prevEnd && existingNames.Contains(x.Customer))
                .Sum(x => x.NetAmount);

            // ── 4) รายละเอียด Lost customer + trailing-12M revenue/GP ก่อนวันซื้อครั้งสุดท้าย — จาก byCustomer เดิม ──
            var lostCustomerRows = new List<LostCustomerRowDto>();
            foreach (var c in lost)
            {
                var rows = byCustomer.TryGetValue(c.Customer, out var r) ? r : new List<ChurnRawRow>();
                var windowStart = c.LastPurchase.AddDays(-364);
                var windowRows = rows.Where(x => x.Date >= windowStart && x.Date <= c.LastPurchase).ToList();
                var last12MRevenue = windowRows.Sum(x => x.NetAmount);
                var last12MGrossProfit = windowRows.Sum(x => x.GrossProfit);
                var days = (int)(asOfDate.Date - c.LastPurchase.Date).TotalDays;
                var riskLevel = GetRiskLevel(days);
                var latest = rows.Count > 0 ? rows[^1] : default;

                lostCustomerRows.Add(new LostCustomerRowDto
                {
                    CustomerCode = latest?.SoldToParty ?? "",
                    CustomerName = c.Customer,
                    IndustryName = latest?.IndustryName ?? "Unknown",
                    SalesEmployeeBP = latest?.SalesEmployeeBP ?? "",
                    LastPurchaseDate = c.LastPurchase,
                    DaysSinceLastPurchase = days,
                    RiskLevel = riskLevel,
                    RiskScore = GetRiskScore(days),
                    Last12MRevenue = last12MRevenue,
                    Last12MGrossProfit = last12MGrossProfit,
                    Action = GetAction(riskLevel)
                });
            }
            lostCustomerRows = lostCustomerRows.OrderByDescending(x => x.DaysSinceLastPurchase).ToList();

            var lostRevenue = lostCustomerRows.Sum(x => x.Last12MRevenue);
            var lostGrossProfit = lostCustomerRows.Sum(x => x.Last12MGrossProfit);

            // ── 5) Churn Rate Trend รายเดือน — point-in-time เทียบ cohort Existing คงที่ทั้งปี ──
            var maxMonth = analysisYear == DateTime.Now.Year ? DateTime.Now.Month : 12;
            var months = await _db.MsMonths.AsNoTracking()
                .Where(m => m.MonthId <= maxMonth)
                .OrderBy(m => m.MonthId)
                .ToListAsync(ct);

            var churnTrend = new List<ChurnTrendPointDto>();
            if (existing.Count > 0)
            {
                foreach (var m in months)
                {
                    var cutoff = new DateTime(analysisYear, m.MonthId, DateTime.DaysInMonth(analysisYear, m.MonthId));
                    if (cutoff > asOfDate) cutoff = asOfDate;

                    var lostAsOfCount = 0;
                    foreach (var c in existing)
                    {
                        var dates = byCustomer.TryGetValue(c.Customer, out var rr)
                            ? rr.Select(x => x.Date)
                            : new[] { c.FirstPurchase };
                        var lastAsOf = dates.Where(d => d <= cutoff).DefaultIfEmpty(c.FirstPurchase).Max();
                        if ((cutoff - lastAsOf.Date).TotalDays >= LostThresholdDays) lostAsOfCount++;
                    }

                    churnTrend.Add(new ChurnTrendPointDto
                    {
                        MonthId = m.MonthId,
                        MonthName = m.MonthNameEN,
                        ChurnRatePercent = Math.Round(lostAsOfCount * 100m / existing.Count, 1)
                    });
                }
            }

            // ── 6) Churn by Industry (Top 5 + Other) ─────────────────────────────────────
            var churnByIndustry = new List<ChurnByGroupRowDto>();
            if (existing.Count > 0)
            {
                var existingByIndustry = existing
                    .Select(c => new { c.Customer, Industry = RepresentativeIndustry(c.Customer) })
                    .GroupBy(x => x.Industry)
                    .Select(g => new { Industry = g.Key, Customers = g.Select(x => x.Customer).ToHashSet() })
                    .OrderByDescending(g => g.Customers.Count)
                    .ToList();

                var groups = existingByIndustry.Take(5)
                    .Select(g => (g.Industry, g.Customers))
                    .ToList();

                var rest = existingByIndustry.Skip(5).ToList();
                if (rest.Count > 0)
                {
                    var otherCustomers = new HashSet<string>();
                    foreach (var g in rest) otherCustomers.UnionWith(g.Customers);
                    groups.Add(("Other", otherCustomers));
                }

                foreach (var (industry, customers) in groups)
                {
                    var lostInGroup = customers.Count(c => lostNames.Contains(c));
                    churnByIndustry.Add(new ChurnByGroupRowDto
                    {
                        GroupName = industry,
                        ExistingCustomers = customers.Count,
                        LostCustomers = lostInGroup,
                        ChurnRatePercent = customers.Count == 0 ? 0 : Math.Round(lostInGroup * 100m / customers.Count, 1)
                    });
                }
                churnByIndustry = churnByIndustry.OrderByDescending(x => x.ChurnRatePercent).ToList();
            }

            // ── 7) Risk Summary (เฉพาะกลุ่ม Lost: Watch/High/Critical) ───────────────────
            var riskSummary = lostCustomerRows
                .GroupBy(x => x.RiskLevel)
                .Select(g => new RiskSummaryRowDto
                {
                    RiskLevel = g.Key,
                    CustomerCount = g.Count(),
                    RevenueAtRisk = g.Sum(x => x.Last12MRevenue)
                })
                .OrderByDescending(x => x.RevenueAtRisk)
                .ToList();

            var totalRiskRevenue = riskSummary.Sum(x => x.RevenueAtRisk);
            foreach (var row in riskSummary)
                row.SharePercent = totalRiskRevenue == 0 ? 0 : Math.Round(row.RevenueAtRisk * 100m / totalRiskRevenue, 1);

            var totalRevenue = existingRevenue + newRevenue;

            return new CustomerChurnAnalysisDto
            {
                AsOfDate = asOfDate,
                Kpi = new ChurnKpiDto
                {
                    ExistingCustomers = existing.Count,
                    ActiveExistingCustomers = existing.Count - lost.Count,
                    LostCustomers = lost.Count,
                    NewCustomers = newCust.Count,
                    TotalCustomerBase = existing.Count + newCust.Count,
                    ChurnRatePercent = existing.Count == 0 ? 0 : Math.Round(lost.Count * 100m / existing.Count, 1),
                    RetentionRatePercent = existing.Count == 0 ? 0 : Math.Round(100 - (lost.Count * 100m / existing.Count), 1),
                    ExistingRevenue = existingRevenue,
                    NewRevenue = newRevenue,
                    TotalRevenue = totalRevenue,
                    ExistingRevenueSharePercent = totalRevenue == 0 ? 0 : Math.Round(existingRevenue * 100m / totalRevenue, 1),
                    PreviousExistingRevenue = previousExistingRevenue,
                    ExistingGrowthPercent = previousExistingRevenue == 0 ? 0 : Math.Round((existingRevenue - previousExistingRevenue) * 100m / previousExistingRevenue, 1),
                    LostRevenue = lostRevenue,
                    LostGrossProfit = lostGrossProfit
                },
                ChurnTrend = churnTrend,
                ChurnByIndustry = churnByIndustry,
                LostCustomers = lostCustomerRows,
                RiskSummary = riskSummary,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableCustomerGroups = availableCustomerGroups,
                AvailableIndustries = availableIndustries
            };
        }

        // ── query หลักตัวเดียวที่ยิงไปที่ MGT_Sale — ดึงประวัติการซื้อทั้งหมดของลูกค้าใน scope มาในครั้งเดียว ──
        // (จำเป็นต้องดึงทั้งประวัติ ไม่ใช่แค่ปีที่วิเคราะห์ เพราะต้องรู้ First/Last Purchase ที่แท้จริงของลูกค้าแต่ละราย)
        private static async Task<List<ChurnRawRow>> GetRawRowsAsync(IQueryable<MGT_Sale> scopeQuery, CancellationToken ct)
        {
            var rows = await scopeQuery
                .Where(x => x.CustomerFullName != null && x.CustomerFullName != "" && x.BillingDocumentDate.HasValue)
                .Select(x => new
                {
                    Customer = x.CustomerFullName!,
                    Date = x.BillingDocumentDate!.Value,
                    NetAmount = x.NetAmount ?? 0,
                    GrossProfit = x.GrossProfit ?? 0,
                    x.IndustryName,
                    x.SalesEmployeeBP,
                    x.SoldToParty
                })
                .ToListAsync(ct);

            return rows.Select(r => new ChurnRawRow(r.Customer, r.Date, r.NetAmount, r.GrossProfit, r.IndustryName, r.SalesEmployeeBP, r.SoldToParty)).ToList();
        }

        private static async Task<(List<string> SalesEmployees, List<string> CustomerGroups, List<string> Industries)>
            GetDropdownsAsync(IQueryable<MGT_Sale> dropdownScopeQuery, CancellationToken ct)
        {
            var raw = await dropdownScopeQuery
                .Select(x => new { x.SalesEmployeeBP, x.AffiliateCustomerName, x.IndustryName })
                .Distinct()
                .ToListAsync(ct);

            static List<string> DistinctSorted(IEnumerable<string?> values) =>
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

            return (
                DistinctSorted(raw.Select(x => x.SalesEmployeeBP)),
                DistinctSorted(raw.Select(x => x.AffiliateCustomerName)),
                DistinctSorted(raw.Select(x => x.IndustryName))
            );
        }

        // Risk Level ตามตาราง: <365 Active (ไม่ถูกนับใน Lost), 365-699 Watch, 700-999 High, >=1000 Critical
        private static string GetRiskLevel(int days)
        {
            if (days >= CriticalThresholdDays) return "Critical";
            if (days >= HighThresholdDays) return "High";
            return "Watch";
        }

        private static string GetAction(string riskLevel) => riskLevel switch
        {
            "Critical" => "Win-back Now",
            "High" => "Call / Visit",
            _ => "Monitor"
        };

        // Risk Score = ความเก่าของการไม่ซื้อ normalize เป็น 0-100 (วันละ ~0.1 คะแนน, เพดาน 100 ที่ 1,000 วัน)
        // เป็นตัวเลขที่คำนวณจาก Days Since Last Purchase ตรงๆ เพื่อความโปร่งใส ไม่ได้อิงยอดขาย/กำไร
        private static int GetRiskScore(int days) => Math.Min(100, (int)Math.Round(days / 10.0));

        // ไม่ใส่ filter ปี — cohort Existing/New ต้องอาศัยประวัติการซื้อทั้งหมดของลูกค้า ไม่ใช่แค่ปีที่เลือก
        private static IQueryable<MGT_Sale> ApplyScopeFilter(IQueryable<MGT_Sale> query, CustomerChurnAnalysisFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.SalesGroup))
                query = query.Where(x => x.SalesGroup == filter.SalesGroup);

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.CustomerGroup))
                query = query.Where(x => x.AffiliateCustomerName == filter.CustomerGroup);

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            return query;
        }
    }
}
