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
    public class SalePerformanceService : ISalePerformanceService
    {
        private readonly AppDbContext _db;
        private readonly ISalesEmployeeNameResolver _nameResolver;

        public SalePerformanceService(AppDbContext db, ISalesEmployeeNameResolver nameResolver)
        {
            _db = db;
            _nameResolver = nameResolver;
        }

        private const int YearsBack = 5;   // ตาราง 1 แสดง ปีปัจจุบัน + ย้อนหลัง 5 ปี = 6 ปี

        public async Task<SalePerformanceDto> GetAsync(SalePerformanceFilter filter, CancellationToken ct = default)
        {
            // ★ นิยาม BU ใหม่ทั้งหน้า — อ้างอิงจากรายชื่อพนักงานขาย Active ของ BU นั้น (Ms_User.Division + Department='Sales')
            // แทนการเชื่อ SalesGroup ของธุรกรรมเอง (ดู comment ที่ ISalesEmployeeNameResolver/SalesOverviewService)
            var lockedBuNames = string.IsNullOrWhiteSpace(filter.SalesGroup)
                ? null
                : await _nameResolver.GetActiveSalesEmployeeFullNamesAsync(filter.SalesGroup, ct);

            // DbContext ไม่ thread-safe — await ทีละตัว
            var byYear = await GetByYearAsync(filter, lockedBuNames, ct);
            var byMonth = await GetByMonthAsync(filter, lockedBuNames, ct);
            var trend = await GetEmployeeTrendAsync(filter, lockedBuNames, ct);

            static SalePerfTotalDto Mk(decimal net, decimal gp) => new()
            {
                TotalRevenue = net,
                TotalGrossProfit = gp,
                TotalPercentMargin = net == 0 ? 0 : Math.Round(gp / net * 100, 2)
            };

            return new SalePerformanceDto
            {
                ByYear = byYear,
                ByMonth = byMonth,
                EmployeeTrend = trend,

                // ---- ค่ารวม (คำนวณฝั่ง backend จากชุดข้อมูลที่กรอง BU แล้ว) ----
                ByYearRowTotals = byYear.GroupBy(r => r.SaleEM)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ByYearColTotals = byYear.GroupBy(r => r.Year)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ByYearGrandTotal = Mk(byYear.Sum(x => x.TotalRevenue), byYear.Sum(x => x.TotalGrossProfit)),

                ByMonthRowTotals = byMonth.GroupBy(r => r.SaleEM)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ByMonthColTotals = byMonth.GroupBy(r => r.MonthId)
                    .ToDictionary(g => g.Key, g => Mk(g.Sum(x => x.TotalRevenue), g.Sum(x => x.TotalGrossProfit))),
                ByMonthGrandTotal = Mk(byMonth.Sum(x => x.TotalRevenue), byMonth.Sum(x => x.TotalGrossProfit))
            };
        }

        // ---------- ตาราง 1: คน × ปี ----------
        private async Task<List<SalePerfByYearRowDto>> GetByYearAsync(SalePerformanceFilter filter, HashSet<string>? lockedBuNames, CancellationToken ct)
        {
            int currentYear = DateTime.Now.Year;
            int startYear = currentYear - YearsBack;

            var q = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames)
                .Where(x => x.BillingDocumentDate.HasValue
                         && x.BillingDocumentDate.Value.Year >= startYear
                         && x.BillingDocumentDate.Value.Year <= currentYear);

            var rows = await q
                .GroupBy(x => new { x.SalesEmployeeBP, Y = x.BillingDocumentDate!.Value.Year })
                .Select(g => new
                {
                    g.Key.SalesEmployeeBP,
                    g.Key.Y,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            return rows
                .OrderBy(r => r.SalesEmployeeBP).ThenBy(r => r.Y)
                .Select(r => new SalePerfByYearRowDto
                {
                    SaleEM = r.SalesEmployeeBP ?? "(N/A)",
                    Year = r.Y,
                    TotalRevenue = r.Net,
                    TotalGrossProfit = r.Gp,
                    TotalPercentMargin = r.Net == 0 ? 0 : Math.Round(r.Gp / r.Net * 100, 2)
                })
                .ToList();
        }

        // ---------- ตาราง 2: คน × เดือน ----------
        private async Task<List<SalePerfByMonthRowDto>> GetByMonthAsync(SalePerformanceFilter filter, HashSet<string>? lockedBuNames, CancellationToken ct)
        {
            var q = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames)
                .Where(x => x.BillingDocumentDate.HasValue);

            var rows = await q
                .GroupBy(x => new { x.SalesEmployeeBP, M = x.BillingDocumentDate!.Value.Month })
                .Select(g => new
                {
                    g.Key.SalesEmployeeBP,
                    g.Key.M,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            var months = await _db.MsMonths.AsNoTracking().ToListAsync(ct);
            string MonthName(int id) => months.FirstOrDefault(m => m.MonthId == id)?.MonthNameEN ?? id.ToString();

            return rows
                .OrderBy(r => r.SalesEmployeeBP).ThenBy(r => r.M)
                .Select(r => new SalePerfByMonthRowDto
                {
                    SaleEM = r.SalesEmployeeBP ?? "(N/A)",
                    MonthId = r.M,
                    MonthName = MonthName(r.M),
                    TotalRevenue = r.Net,
                    TotalGrossProfit = r.Gp,
                    TotalPercentMargin = r.Net == 0 ? 0 : Math.Round(r.Gp / r.Net * 100, 2)
                })
                .ToList();
        }

        // ---------- Chart 3: ต่อคน (X = Sale Employee) ----------
        private async Task<List<SalePerfEmployeeDto>> GetEmployeeTrendAsync(SalePerformanceFilter filter, HashSet<string>? lockedBuNames, CancellationToken ct)
        {
            var q = ApplyFilter(_db.MGT_Sale.AsNoTracking(), filter, lockedBuNames);

            var rows = await q
                .GroupBy(x => x.SalesEmployeeBP)
                .Select(g => new
                {
                    SalesEmployeeBP = g.Key,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            return rows
                .OrderByDescending(r => r.Net)
                .Select(r => new SalePerfEmployeeDto
                {
                    SaleEM = r.SalesEmployeeBP ?? "(N/A)",
                    TotalRevenue = r.Net,
                    TotalPercentMargin = r.Net == 0 ? 0 : Math.Round(r.Gp / r.Net * 100, 2)
                })
                .ToList();
        }

        // ---------- BU list (สำหรับ manager ใหญ่ที่ดูได้ทุก BU) ----------
        public async Task<List<string>> GetDistinctBusAsync(CancellationToken ct = default)
        {
            return await _db.MGT_Sale.AsNoTracking()
                .Where(x => x.SalesGroup != null && x.SalesGroup != "")
                .Select(x => x.SalesGroup!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);
        }

        private static IQueryable<MGT_Sale> ApplyFilter(IQueryable<MGT_Sale> q, SalePerformanceFilter f, HashSet<string>? lockedBuNames)
        {
            // ปี (จาก BillingDocumentDate ให้สอดคล้องกับหน้าอื่น)
            if (f.Year.HasValue)
                q = q.Where(x => x.BillingDocumentDate.HasValue
                              && x.BillingDocumentDate.Value.Year == f.Year.Value);

            // BU — กรองจากรายชื่อพนักงานขาย Active ของ BU นั้น (ไม่ใช่ SalesGroup ธุรกรรม) ดู comment ที่ GetAsync
            if (lockedBuNames is not null)
                q = q.Where(x => x.SalesEmployeeBP != null && lockedBuNames.Contains(x.SalesEmployeeBP));

            if (!string.IsNullOrWhiteSpace(f.SalesEmployeeBP))
                q = q.Where(x => x.SalesEmployeeBP == f.SalesEmployeeBP);

            return q;
        }
    }
}
