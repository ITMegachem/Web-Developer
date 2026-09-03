using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Data;   // ⚠️ ปรับ using ของ AppDbContext ให้ตรงกับ service อื่น

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class CustomerOverviewService : ICustomerOverviewService
    {
        private readonly AppDbContext _db;
        public CustomerOverviewService(AppDbContext db) => _db = db;

        public async Task<CustomerOverviewDto> GetAsync(CustomerOverviewFilter filter, CancellationToken ct = default)
        {
            // ---------- matrix: Customer × เดือน ----------
            var q = _db.MGT_Sale.AsNoTracking();
            q = ApplyYearBu(q, filter);
            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                q = q.Where(x => x.IndustryName == filter.IndustryName);
            if (!string.IsNullOrWhiteSpace(filter.CustomerFullName))
                q = q.Where(x => x.CustomerFullName == filter.CustomerFullName);

            q = q.Where(x => x.BillingDocumentDate.HasValue);

            var rows = await q
                .GroupBy(x => new { x.CustomerFullName, M = x.BillingDocumentDate!.Value.Month })
                .Select(g => new
                {
                    g.Key.CustomerFullName,
                    g.Key.M,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            var months = await _db.MsMonths.AsNoTracking().ToListAsync(ct);
            string MonthName(int id) => months.FirstOrDefault(m => m.MonthId == id)?.MonthNameEN ?? id.ToString();

            var byMonth = rows
                .OrderBy(r => r.CustomerFullName).ThenBy(r => r.M)
                .Select(r => new CustomerByMonthRowDto
                {
                    Customer = r.CustomerFullName ?? "(N/A)",
                    MonthId = r.M,
                    MonthName = MonthName(r.M),
                    TotalRevenue = r.Net,
                    TotalGrossProfit = r.Gp,
                    TotalPercentMargin = r.Net == 0 ? 0 : Math.Round(r.Gp / r.Net * 100, 2)
                })
                .ToList();

            // ---------- ตัวเลือก dropdown ----------
            // Industry: scope แค่ BU + Year
            var indBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter);
            var industries = await indBase
                .Where(x => x.IndustryName != null && x.IndustryName != "")
                .Select(x => x.IndustryName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            // Customer: scope BU + Year + Industry ที่เลือก (cascade)
            var custBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter);
            if (!string.IsNullOrWhiteSpace(filter.IndustryName))
                custBase = custBase.Where(x => x.IndustryName == filter.IndustryName);
            var customers = await custBase
                .Where(x => x.CustomerFullName != null && x.CustomerFullName != "")
                .Select(x => x.CustomerFullName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            static SalePerfTotalDto Mk(decimal net, decimal gp) => new()
            {
                TotalRevenue = net,
                TotalGrossProfit = gp,
                TotalPercentMargin = net == 0 ? 0 : Math.Round(gp / net * 100, 2)
            };

            return new CustomerOverviewDto
            {
                ByMonth = byMonth,
                Industries = industries,
                Customers = customers,

                RowTotals = byMonth.GroupBy(r => r.Customer)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ColTotals = byMonth.GroupBy(r => r.MonthId)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                GrandTotal = Mk(byMonth.Sum(x => x.TotalRevenue), byMonth.Sum(x => x.TotalGrossProfit))
            };
        }

        // filter พื้นฐาน: ปี (จาก BillingDocumentDate) + BU (SalesGroup, server บังคับ)
        // ---------- BU list (manager ใหญ่ที่ดูได้ทุก BU) ----------
        public async Task<List<string>> GetDistinctBusAsync(CancellationToken ct = default)
        {
            return await _db.MGT_Sale.AsNoTracking()
                .Where(x => x.SalesGroup != null && x.SalesGroup != "")
                .Select(x => x.SalesGroup!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);
        }

        private static IQueryable<MGT_Sale> ApplyYearBu(IQueryable<MGT_Sale> q, CustomerOverviewFilter f)
        {
            if (f.Year.HasValue)
                q = q.Where(x => x.BillingDocumentDate.HasValue
                              && x.BillingDocumentDate.Value.Year == f.Year.Value);
            if (!string.IsNullOrWhiteSpace(f.SalesGroup))
                q = q.Where(x => x.SalesGroup == f.SalesGroup);
            return q;
        }
    }
}
