using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Data;   // ⚠️ ปรับ using ของ AppDbContext ให้ตรงกับ SalesOverviewService

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class ProductOverviewService : IProductOverviewService
    {
        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public ProductOverviewService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        public async Task<ProductOverviewDto> GetAsync(ProductOverviewFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งหน้า — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.SalesGroup, ct);

            // ---------- matrix: Material × เดือน (กรองครบทุก filter) ----------
            var q = _db.MGT_Sale.AsNoTracking();
            q = ApplyYearBu(q, filter, lockedBuNames);
            if (!string.IsNullOrWhiteSpace(filter.MaterialGroupName))
                q = q.Where(x => x.MaterialGroupName == filter.MaterialGroupName);
            if (!string.IsNullOrWhiteSpace(filter.MaterialName))
                q = q.Where(x => x.MaterialName == filter.MaterialName);

            q = q.Where(x => x.BillingDocumentDate.HasValue);

            var rows = await q
                .GroupBy(x => new { x.MaterialName, M = x.BillingDocumentDate!.Value.Month })
                .Select(g => new
                {
                    g.Key.MaterialName,
                    g.Key.M,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            var months = await _db.MsMonths.AsNoTracking().ToListAsync(ct);
            string MonthName(int id) => months.FirstOrDefault(m => m.MonthId == id)?.MonthNameEN ?? id.ToString();

            var byMonth = rows
                .OrderBy(r => r.MaterialName).ThenBy(r => r.M)
                .Select(r => new ProductByMonthRowDto
                {
                    MaterialName = r.MaterialName ?? "(N/A)",
                    MonthId = r.M,
                    MonthName = MonthName(r.M),
                    TotalRevenue = r.Net,
                    TotalGrossProfit = r.Gp,
                    TotalPercentMargin = r.Net == 0 ? 0 : Math.Round(r.Gp / r.Net * 100, 2)
                })
                .ToList();

            // ---------- ตัวเลือก dropdown (cascade) ----------
            // Material Group: กรองแค่ BU + Year
            var groupBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            var materialGroups = await groupBase
                .Where(x => x.MaterialGroupName != null && x.MaterialGroupName != "")
                .Select(x => x.MaterialGroupName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            // Material Name: กรอง BU + Year + Group ที่เลือก (ไม่กรองด้วย MaterialName เอง)
            var matBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            if (!string.IsNullOrWhiteSpace(filter.MaterialGroupName))
                matBase = matBase.Where(x => x.MaterialGroupName == filter.MaterialGroupName);
            var materials = await matBase
                .Where(x => x.MaterialName != null && x.MaterialName != "")
                .Select(x => x.MaterialName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            static SalePerfTotalDto Mk(decimal net, decimal gp) => new()
            {
                TotalRevenue = net,
                TotalGrossProfit = gp,
                TotalPercentMargin = net == 0 ? 0 : Math.Round(gp / net * 100, 2)
            };

            return new ProductOverviewDto
            {
                ByMonth = byMonth,
                MaterialGroups = materialGroups,
                Materials = materials,

                RowTotals = byMonth.GroupBy(r => r.MaterialName)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ColTotals = byMonth.GroupBy(r => r.MonthId)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                GrandTotal = Mk(byMonth.Sum(x => x.TotalRevenue), byMonth.Sum(x => x.TotalGrossProfit))
            };
        }

        // filter พื้นฐาน: ปี (จาก BillingDocumentDate) + BU (รายชื่อพนักงานขาย Active ของ BU นั้น)
        private static IQueryable<MGT_Sale> ApplyYearBu(IQueryable<MGT_Sale> q, ProductOverviewFilter f, HashSet<string>? lockedBuNames)
        {
            if (f.Year.HasValue)
                q = q.Where(x => x.BillingDocumentDate.HasValue
                              && x.BillingDocumentDate.Value.Year == f.Year.Value);
            if (lockedBuNames is not null)
                q = q.Where(x => x.SalesEmployeeBP != null && lockedBuNames.Contains(x.SalesEmployeeBP));
            if (!string.IsNullOrWhiteSpace(f.SalesEmployeeBP))
                q = q.Where(x => x.SalesEmployeeBP == f.SalesEmployeeBP);
            return q;
        }
    }
}
