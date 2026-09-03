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
    public class SalesOverviewService : ISalesOverviewService
    {
        private readonly AppDbContext _db;

        public SalesOverviewService(AppDbContext db) => _db = db;

        public async Task<SalesOverviewDto> GetOverviewAsync(SalesOverviewFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            // บน DbContext ตัวเดียวกัน ต้อง await ทีละตัวแบบนี้เท่านั้น
            var summary = await GetSummaryAsync(filter, ct);
            var bySalesman = await GetBreakdownAsync(filter, x => x.SalesEmployeeBP, ct);
            var byMaterialGroup = await GetBreakdownAsync(filter, x => x.MaterialGroupName, ct);
            var byAffiliate = await GetBreakdownAsync(filter, x => x.AffiliateCustomerName, ct);
            var byIndustry = await GetBreakdownAsync(filter, x => x.IndustryName, ct);
            var monthly = await GetMonthlyRevenueAsync(filter, ct);

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

        private async Task<List<BreakdownRowDto>> GetBreakdownAsync(
            SalesOverviewFilter filter,
            Expression<Func<MGT_Sale, string?>> keySelector,
            CancellationToken ct)
        {
            var query = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter);

            var rows = await query
                .GroupBy(keySelector)
                .Select(g => new
                {
                    Key = g.Key,
                    NetAmount = g.Sum(x => x.NetAmount ?? 0),
                    CostAmount = g.Sum(x => x.CostAmount ?? 0),
                    GrossProfit = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            return rows.Select(r => new BreakdownRowDto
            {
                Key = r.Key,
                NetAmount = r.NetAmount,
                CostAmount = r.CostAmount,
                GrossProfit = r.GrossProfit,
                MarginPercent = r.NetAmount == 0 ? 0 : Math.Round(r.GrossProfit / r.NetAmount * 100, 2)
            }).ToList();
        }

        private async Task<SalesSummaryDto> GetSummaryAsync(SalesOverviewFilter filter, CancellationToken ct)
        {
            var query = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter);

            var totals = await query
                .GroupBy(x => 1)
                .Select(g => new
                {
                    TotalRevenue = g.Sum(x => x.NetAmount ?? 0),
                    TotalCogs = g.Sum(x => x.CostAmount ?? 0),
                    TotalGrossProfit = g.Sum(x => x.GrossProfit ?? 0),
                    TotalBU1 = g.Sum(x => x.SalesGroup == "BU1" ? (x.NetAmount ?? 0) : 0),
                    TotalBU2 = g.Sum(x => x.SalesGroup == "BU2" ? (x.NetAmount ?? 0) : 0),
                    TotalBU3 = g.Sum(x => x.SalesGroup == "BU3" ? (x.NetAmount ?? 0) : 0),
                    TotalBU4 = g.Sum(x => x.SalesGroup == "BU4" ? (x.NetAmount ?? 0) : 0),
                    TotalOther = g.Sum(x =>
                        x.SalesGroup != "BU1" && x.SalesGroup != "BU2" &&
                        x.SalesGroup != "BU3" && x.SalesGroup != "BU4"
                        ? (x.NetAmount ?? 0) : 0)
                })
                .FirstOrDefaultAsync(ct);

            // COUNT(DISTINCT ...) แยกออกมาต่างหาก เพราะรวมกับ conditional-sum ด้านบนในคำสั่งเดียวจะซับซ้อนและเสี่ยงแปลง SQL ไม่ผ่าน
            var totalProduct = await query.Select(x => x.Material).Distinct().CountAsync(ct);
            var totalCustomer = await query.Select(x => x.CustomerFullName).Distinct().CountAsync(ct);

            if (totals is null) return new SalesSummaryDto();

            return new SalesSummaryDto
            {
                TotalRevenue = totals.TotalRevenue,
                TotalCogs = totals.TotalCogs,
                TotalGrossProfit = totals.TotalGrossProfit,
                GrossProfitMarginPercent = totals.TotalRevenue == 0 ? 0 : Math.Round(totals.TotalGrossProfit / totals.TotalRevenue * 100, 2),
                TotalProduct = totalProduct,
                TotalCustomer = totalCustomer,
                TotalBU1 = totals.TotalBU1,
                TotalBU2 = totals.TotalBU2,
                TotalBU3 = totals.TotalBU3,
                TotalBU4 = totals.TotalBU4,
                TotalOther = totals.TotalOther
            };
        }



        private async Task<List<MonthlyRevenueDto>> GetMonthlyRevenueAsync(SalesOverviewFilter filter, CancellationToken ct)
        {
            var query = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter);

            var byMonth = await query
                .Where(x => x.BillingDocumentDate.HasValue)
                .GroupBy(x => x.BillingDocumentDate!.Value.Month)
                .Select(g => new
                {
                    MonthId = g.Key,
                    Revenue = g.Sum(x => x.NetAmount ?? 0),
                    GrossProfit = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            var selectedYear = filter.Year ?? DateTime.Now.Year;

            var maxMonth = selectedYear == DateTime.Now.Year ? DateTime.Now.Month : 12;

            var months = await _db.MsMonths.AsNoTracking()
                .Where(m => m.MonthId <= maxMonth)
                .OrderBy(m => m.MonthId)
                .ToListAsync(ct);

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
        private static IQueryable<MGT_Sale> ApplyFilter(IQueryable<MGT_Sale> query, SalesOverviewFilter filter)
        {
            // ★ กรองตาม BU (SalesGroup) — ค่านี้ controller เป็นคน resolve จาก permission/DataScope มาแล้ว
            //   (client ส่งอะไรมาก็ถูกเขียนทับที่ controller เพื่อกัน spoof)
            if (!string.IsNullOrWhiteSpace(filter.Bu))
                query = query.Where(x => x.SalesGroup == filter.Bu);

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
