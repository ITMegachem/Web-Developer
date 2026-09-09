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
    // ★★★ Performance tuning (2026-09) ★★★ — MGT_Sale เป็น VIEW ที่ index ไม่ได้ (ดู comment เดียวกันใน
    // CrossSellUpsellGainsService/PricingMarginPerformanceService/CustomerChurnAnalysisService) เดิม service นี้ยิง
    // query แยกไปที่ MGT_Sale 8 ครั้งต่อการโหลด 1 ครั้ง (Summary 3 query, Breakdown 4 field แยกกัน 4 query, Monthly 1 query)
    // ทั้งที่ทุกตัวใช้ scope/filter เดียวกันเป๊ะ แค่ group คนละแบบ — เพจนี้เป็นหน้า Dashboard หลักที่โหลดถี่ที่สุด
    // จึงคุ้มที่จะรวมเหลือ query เดียว โดยดึง raw rows มาครั้งเดียวแล้ว group/sum ทุกแบบในหน่วยความจำแทน
    public class SalesOverviewService : ISalesOverviewService
    {
        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public SalesOverviewService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private sealed record RawSaleRow(
            string? SalesEmployeeBP, string? MaterialGroupName, string? AffiliateCustomerName, string? IndustryName,
            string? SalesGroup, string? Material, string? CustomerFullName, int? Month,
            decimal NetAmount, decimal CostAmount, decimal GrossProfit);

        public async Task<SalesOverviewDto> GetOverviewAsync(SalesOverviewFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม "BU" ใหม่ทั้งหน้า — อ้างอิงจาก Ms_User.Division + Department='Sales' (พนักงานขาย Active ที่สังกัด
            // BU นั้นจริง) แทนการเชื่อ MGT_Sale.SalesGroup ของธุรกรรมเอง (ดู comment ที่ ISalesEmployeeNameResolver)
            // เมื่อ filter.Bu ถูกล็อก (Leader) ต้องกรองด้วยรายชื่อพนักงานของ BU นั้นก่อนสร้าง query หลัก
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.Bu)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.Bu, ct);

            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var query = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            var raw = await GetRawRowsAsync(query, ct);

            // ★ Total BU1-4/Other ก็ต้องจัดประเภทตาม Division ของพนักงานขายจริงเช่นกัน (ไม่ใช่ SalesGroup ของธุรกรรม)
            // ธุรกรรมที่ไม่ตรงกับพนักงานขาย Active คนไหนเลย ถูกตัดออกจากทุก BU (ไม่นับใน Other ด้วย) ตามที่ยืนยันแล้ว
            var divisionMap = await _nameResolver.GetActiveSalesEmployeeDivisionMapAsync(ct);
            var summary = BuildSummary(raw, divisionMap);

            // ★ "Total Revenue by Salesman" แสดงชื่อพนักงานขายจริงตามธุรกรรมเสมอ (ไม่กรอง/ไม่รวมกลุ่มตาม Home Division)
            // — ยืนยันแล้วว่าต้องการแบบนี้สุดท้าย เพื่อไม่ให้ Total ไม่ตรงกับยอดขายจริงทั้งหมดของ BU
            var bySalesman = BuildBreakdown(raw, x => x.SalesEmployeeBP);
            var byMaterialGroup = BuildBreakdown(raw, x => x.MaterialGroupName);
            var byAffiliate = BuildBreakdown(raw, x => x.AffiliateCustomerName);
            var byIndustry = BuildBreakdown(raw, x => x.IndustryName);

            var selectedYear = filter.Year ?? DateTime.Now.Year;
            var maxMonth = selectedYear == DateTime.Now.Year ? DateTime.Now.Month : 12;
            var months = await _db.MsMonths.AsNoTracking()
                .Where(m => m.MonthId <= maxMonth)
                .OrderBy(m => m.MonthId)
                .ToListAsync(ct);
            var monthly = BuildMonthlyRevenue(raw, months);

            return new SalesOverviewDto
            {
                Summary = summary,
                BySalesman = bySalesman,
                ByMaterialGroup = byMaterialGroup,
                ByAffiliate = byAffiliate,
                ByIndustry = byIndustry,
                MonthlyRevenue = monthly
            };
        }

        // ── query เดียวที่ยิงไปที่ MGT_Sale — ดึงทุกคอลัมน์ที่ Summary/Breakdown/Monthly ต้องใช้มาในครั้งเดียว ──
        private static async Task<List<RawSaleRow>> GetRawRowsAsync(IQueryable<MGT_Sale> query, CancellationToken ct)
        {
            var rows = await query
                .Select(x => new
                {
                    x.SalesEmployeeBP,
                    x.MaterialGroupName,
                    x.AffiliateCustomerName,
                    x.IndustryName,
                    x.SalesGroup,
                    x.Material,
                    x.CustomerFullName,
                    Month = x.BillingDocumentDate.HasValue ? (int?)x.BillingDocumentDate.Value.Month : null,
                    NetAmount = x.NetAmount ?? 0,
                    CostAmount = x.CostAmount ?? 0,
                    GrossProfit = x.GrossProfit ?? 0
                })
                .ToListAsync(ct);

            return rows.Select(r => new RawSaleRow(
                r.SalesEmployeeBP, r.MaterialGroupName, r.AffiliateCustomerName, r.IndustryName,
                r.SalesGroup, r.Material, r.CustomerFullName, r.Month, r.NetAmount, r.CostAmount, r.GrossProfit)).ToList();
        }

        private static SalesSummaryDto BuildSummary(List<RawSaleRow> rows, Dictionary<string, string> divisionMap)
        {
            var totalRevenue = rows.Sum(x => x.NetAmount);
            var totalCogs = rows.Sum(x => x.CostAmount);
            var totalGrossProfit = rows.Sum(x => x.GrossProfit);

            // ★ จัดประเภทแต่ละแถวตาม Division ของพนักงานขายจริง (ไม่ใช่ SalesGroup ธุรกรรม) — แถวที่หา Division ไม่เจอ
            // (ไม่ตรงกับพนักงานขาย Active คนไหนเลย) ไม่นับใน BU ไหนเลยแม้แต่ Other (ตัดออกทั้งหมดตามที่ยืนยันแล้ว)
            string? RowDivision(RawSaleRow x) =>
                !string.IsNullOrWhiteSpace(x.SalesEmployeeBP) && divisionMap.TryGetValue(x.SalesEmployeeBP, out var div) ? div : null;

            return new SalesSummaryDto
            {
                TotalRevenue = totalRevenue,
                TotalCogs = totalCogs,
                TotalGrossProfit = totalGrossProfit,
                GrossProfitMarginPercent = totalRevenue == 0 ? 0 : Math.Round(totalGrossProfit / totalRevenue * 100, 2),
                TotalProduct = rows.Select(x => x.Material).Distinct().Count(),
                TotalCustomer = rows.Select(x => x.CustomerFullName).Distinct().Count(),
                TotalBU1 = rows.Where(x => RowDivision(x) == "BU1").Sum(x => x.NetAmount),
                TotalBU2 = rows.Where(x => RowDivision(x) == "BU2").Sum(x => x.NetAmount),
                TotalBU3 = rows.Where(x => RowDivision(x) == "BU3").Sum(x => x.NetAmount),
                TotalBU4 = rows.Where(x => RowDivision(x) == "BU4").Sum(x => x.NetAmount),
                TotalOther = rows.Where(x => RowDivision(x) is not (null or "BU1" or "BU2" or "BU3" or "BU4"))
                    .Sum(x => x.NetAmount)
            };
        }

        private static List<BreakdownRowDto> BuildBreakdown(List<RawSaleRow> rows, Func<RawSaleRow, string?> keySelector)
        {
            return rows
                .GroupBy(keySelector)
                .Select(g => new
                {
                    Key = g.Key,
                    NetAmount = g.Sum(x => x.NetAmount),
                    CostAmount = g.Sum(x => x.CostAmount),
                    GrossProfit = g.Sum(x => x.GrossProfit)
                })
                .Select(r => new BreakdownRowDto
                {
                    Key = r.Key,
                    NetAmount = r.NetAmount,
                    CostAmount = r.CostAmount,
                    GrossProfit = r.GrossProfit,
                    MarginPercent = r.NetAmount == 0 ? 0 : Math.Round(r.GrossProfit / r.NetAmount * 100, 2)
                })
                .ToList();
        }

        private static List<MonthlyRevenueDto> BuildMonthlyRevenue(List<RawSaleRow> rows, List<MsMonth> months)
        {
            var byMonth = rows
                .Where(x => x.Month.HasValue)
                .GroupBy(x => x.Month!.Value)
                .Select(g => new { MonthId = g.Key, Revenue = g.Sum(x => x.NetAmount), GrossProfit = g.Sum(x => x.GrossProfit) })
                .ToList();

            return months.Select(m =>
            {
                var row = byMonth.FirstOrDefault(r => r.MonthId == m.MonthId);
                var revenue = row?.Revenue ?? 0;
                var grossProfit = row?.GrossProfit ?? 0;

                return new MonthlyRevenueDto
                {
                    MonthId = m.MonthId,
                    MonthName = m.MonthNameEN,
                    Revenue = revenue,
                    GrossProfitMarginPercent = revenue == 0 ? 0 : Math.Round(grossProfit / revenue * 100, 2)
                };
            }).ToList();
        }

        private static IQueryable<MGT_Sale> ApplyFilter(IQueryable<MGT_Sale> query, SalesOverviewFilter filter, HashSet<string>? lockedBuNames)
        {
            // ★ กรองตาม BU — ค่า filter.Bu นี้ controller เป็นคน resolve จาก permission/DataScope มาแล้ว (client ส่ง
            // อะไรมาก็ถูกเขียนทับที่ controller เพื่อกัน spoof) แต่การกรองจริงอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น
            // (lockedBuNames) ไม่ใช่ SalesGroup ของธุรกรรมเอง — ดู comment ที่ GetOverviewAsync
            if (lockedBuNames is not null)
                query = query.Where(x => x.SalesEmployeeBP != null && lockedBuNames.Contains(x.SalesEmployeeBP));

            // ตกลงกันไว้ก่อนหน้านี้ว่าไม่เก็บ x_Year เป็น column แยกใน Entity
            // จึง filter ปีผ่าน BillingDocumentDate.Year แทนการอ้าง x_Year ตรง ๆ
            if (filter.Year.HasValue)
                query = query.Where(x => x.BillingDocumentDate.HasValue
                                       && x.BillingDocumentDate.Value.Year == filter.Year.Value);

            if (!string.IsNullOrWhiteSpace(filter.AffiliateCustomerName))
                query = query.Where(x => x.AffiliateCustomerName == filter.AffiliateCustomerName);

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.MaterialGroupName))
                query = query.Where(x => x.MaterialGroupName == filter.MaterialGroupName);

            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                query = query.Where(x => x.IndustryName == filter.IndustryName);

            // ★ cross-filter จากการคลิกแท่งเดือนในกราฟ Revenue and Gross Profit Margin Trend
            if (filter.MonthId.HasValue)
                query = query.Where(x => x.BillingDocumentDate.HasValue
                                       && x.BillingDocumentDate.Value.Month == filter.MonthId.Value);

            return query;
        }
    }
}
