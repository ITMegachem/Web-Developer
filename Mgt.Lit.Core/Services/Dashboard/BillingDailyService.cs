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
    public class BillingDailyService : IBillingDailyService
    {
        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public BillingDailyService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        public async Task<BillingDailyDto> GetAsync(BillingDailyFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งหน้า — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.SalesGroup, ct);

            // ---------- detail grid: billing document × material ----------
            var q = ApplyScope(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);
            if (!string.IsNullOrWhiteSpace(filter.CustomerFullName))
                q = q.Where(x => x.CustomerFullName == filter.CustomerFullName);
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                q = q.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            var raw = await q
                .GroupBy(x => new
                {
                    x.BillingDocument,
                    x.BillingDocumentDate,
                    x.SalesEmployeeBP,
                    x.CustomerFullName,
                    x.Material,
                    x.MaterialName,
                    x.ProductGroup
                })
                .Select(g => new
                {
                    g.Key.BillingDocument,
                    g.Key.BillingDocumentDate,
                    g.Key.SalesEmployeeBP,
                    g.Key.CustomerFullName,
                    g.Key.Material,
                    g.Key.MaterialName,
                    g.Key.ProductGroup,
                    Net = g.Sum(x => x.NetAmount ?? 0)
                })
                .ToListAsync(ct);

            var rows = raw
                .OrderBy(r => r.BillingDocumentDate).ThenBy(r => r.BillingDocument)
                .Select(r => new BillingDailyRowDto
                {
                    BillDoc = r.BillingDocument ?? "",
                    BillDate = r.BillingDocumentDate,
                    SaleEm = r.SalesEmployeeBP ?? "",
                    Customer = r.CustomerFullName ?? "",
                    MaterialCode = r.Material ?? "",
                    MaterialName = r.MaterialName ?? "",
                    ProductGroup = r.ProductGroup ?? "",
                    MonthId = r.BillingDocumentDate.HasValue ? r.BillingDocumentDate.Value.Month : 0,
                    TotalRevenue = r.Net
                })
                .ToList();

            // ---------- ตัวเลือก dropdown (scope ตาม BU + ช่วงวันที่) ----------
            var scope = ApplyScope(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);

            var customers = await scope
                .Where(x => x.CustomerFullName != null && x.CustomerFullName != "")
                .Select(x => x.CustomerFullName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            var salesEmployeesRaw = await scope
                .Where(x => x.SalesEmployeeBP != null && x.SalesEmployeeBP != "")
                .Select(x => x.SalesEmployeeBP!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            // ★ กรองซ้ำด้วย Division ประจำของพนักงานเมื่อ BU ถูกล็อก (ดู comment ที่ ISalesEmployeeNameResolver.FilterToDivisionAsync)
            var salesEmployees = await _nameResolver.FilterToDivisionAsync(salesEmployeesRaw, filter.SalesGroup, ct);

            return new BillingDailyDto
            {
                Rows = rows,
                Customers = customers,
                SalesEmployees = salesEmployees,
                GrandTotal = rows.Sum(r => r.TotalRevenue)
            };
        }

        // scope พื้นฐาน: BU (รายชื่อพนักงานขาย Active ของ BU นั้น) + ช่วงวันที่
        private static IQueryable<MGT_Sale> ApplyScope(IQueryable<MGT_Sale> q, BillingDailyFilter f, HashSet<string>? lockedBuNames)
        {
            if (lockedBuNames is not null)
                q = q.Where(x => x.SalesEmployeeBP != null && lockedBuNames.Contains(x.SalesEmployeeBP));
            if (f.DateFrom.HasValue)
                q = q.Where(x => x.BillingDocumentDate.HasValue && x.BillingDocumentDate.Value >= f.DateFrom.Value);
            if (f.DateTo.HasValue)
            {
                // รวมทั้งวันของ DateTo (เผื่อ BillingDocumentDate มีเวลา)
                var end = f.DateTo.Value.Date.AddDays(1);
                q = q.Where(x => x.BillingDocumentDate.HasValue && x.BillingDocumentDate.Value < end);
            }
            return q;
        }
    }
}