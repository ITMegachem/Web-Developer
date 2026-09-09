using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Data; 

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class ProductMovementService : IProductMovementService
    {
        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public ProductMovementService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        public async Task<ProductMovementDto> GetAsync(ProductMovementFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งหน้า — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.SalesGroup, ct);

            var q = _db.MGT_Sale.AsNoTracking();
            q = ApplyYearBu(q, filter, lockedBuNames);
            if (!string.IsNullOrWhiteSpace(filter.CustomerFullName))
                q = q.Where(x => x.CustomerFullName == filter.CustomerFullName);
            if (!string.IsNullOrWhiteSpace(filter.MaterialName))
                q = q.Where(x => x.MaterialName == filter.MaterialName);

            if (filter.MonthId.HasValue)
                q = q.Where(x => x.BillingDocumentDate.HasValue
                               && x.BillingDocumentDate.Value.Month == filter.MonthId.Value);

            q = q.Where(x => x.BillingDocumentDate.HasValue);

            var rows = await q
                .GroupBy(x => new { x.MaterialName, M = x.BillingDocumentDate!.Value.Month })
                .Select(g => new
                {
                    g.Key.MaterialName,
                    g.Key.M,
                    Qty = g.Sum(x => x.Quantity ?? 0)
                })
                .ToListAsync(ct);

            var months = await _db.MsMonths.AsNoTracking().ToListAsync(ct);
            string MonthName(int id) => months.FirstOrDefault(m => m.MonthId == id)?.MonthNameEN ?? id.ToString();

            var byMonth = rows
                .OrderBy(r => r.MaterialName).ThenBy(r => r.M)
                .Select(r => new ProductMovementRowDto
                {
                    MaterialName = r.MaterialName ?? "(N/A)",
                    MonthId = r.M,
                    MonthName = MonthName(r.M),
                    Quantity = (decimal)r.Qty   
                })
                .ToList();

            var custBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            var customers = await custBase
                .Where(x => x.CustomerFullName != null && x.CustomerFullName != "")
                .Select(x => x.CustomerFullName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            var matBase = ApplyYearBu(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            if (!string.IsNullOrWhiteSpace(filter.CustomerFullName))
                matBase = matBase.Where(x => x.CustomerFullName == filter.CustomerFullName);
            var materials = await matBase
                .Where(x => x.MaterialName != null && x.MaterialName != "")
                .Select(x => x.MaterialName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            return new ProductMovementDto
            {
                ByMonth = byMonth,
                Customers = customers,
                Materials = materials,

                RowTotals = byMonth.GroupBy(r => r.MaterialName).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity)),
                ColTotals = byMonth.GroupBy(r => r.MonthId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity)),
                GrandTotal = byMonth.Sum(x => x.Quantity)
            };
        }

        private static IQueryable<MGT_Sale> ApplyYearBu(IQueryable<MGT_Sale> q, ProductMovementFilter f, HashSet<string>? lockedBuNames)
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
