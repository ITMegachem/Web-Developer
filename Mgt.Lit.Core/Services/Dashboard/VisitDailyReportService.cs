using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class VisitDailyReportService : IVisitDailyReportService
    {
        private const int MaxTrendMonths = 36; // กันลูปยาวเกินไปถ้าไม่ได้กรอง DateFrom/DateTo

        private readonly AppDbContext _db;

        public VisitDailyReportService(AppDbContext db) => _db = db;

        public async Task<VisitDailyReportDto> GetAsync(VisitDailyReportFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var visits = await ApplyScopeFilter(_db.MGT_VisitReport.AsNoTracking(), filter)
                .OrderByDescending(v => v.VisitDate)
                .ToListAsync(ct);

            var visitIds = visits.Select(v => v.VisitReportId).ToList();

            var items = visitIds.Count == 0
                ? new List<MGT_VisitItem>()
                : await _db.MGT_VisitItem.AsNoTracking()
                    .Where(i => visitIds.Contains(i.VisitReportId))
                    .ToListAsync(ct);

            var itemsByVisit = items.GroupBy(i => i.VisitReportId).ToDictionary(g => g.Key, g => g.ToList());

            // ── KPI ──────────────────────────────────────────────────────────────
            var totalVisits = visits.Count;
            var totalCustomers = visits.Select(v => v.CustomerName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count();
            var totalSalespeople = visits.Select(v => v.SalesEmployeeBP).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count();

            var totalProductItems = items.Count;
            var distinctProducts = items.Select(i => i.MaterialName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count();
            var totalCompetitors = items.Count(HasCompetitor);
            var totalPotential = items.Count(i => IsPotential(i.Potential));

            var durations = visits
                .Where(v => v.StartDateTime.HasValue && v.EndDateTime.HasValue && v.EndDateTime > v.StartDateTime)
                .Select(v => (v.EndDateTime!.Value - v.StartDateTime!.Value).TotalMinutes)
                .ToList();

            var kpi = new VisitKpiDto
            {
                TotalVisits = totalVisits,
                TotalCustomersVisited = totalCustomers,
                TotalSalespeople = totalSalespeople,
                TotalProductItemsDiscussed = totalProductItems,
                DistinctProductsDiscussed = distinctProducts,
                AvgProductsPerVisit = totalVisits == 0 ? 0 : Math.Round((double)totalProductItems / totalVisits, 1),
                TotalCompetitorsFound = totalCompetitors,
                TotalPotentialItems = totalPotential,
                AvgVisitDurationMinutes = durations.Count == 0 ? null : Math.Round(durations.Average(), 0)
            };

            // ── Trend รายเดือน ────────────────────────────────────────────────────
            var trend = BuildTrend(visits, itemsByVisit, filter.DateFrom, filter.DateTo);

            // ── Breakdown: Salesperson / Customer ───────────────────────────────
            var bySalesperson = visits
                .Where(v => !string.IsNullOrWhiteSpace(v.SalesEmployeeBP))
                .GroupBy(v => v.SalesEmployeeBP!)
                .Select(g => BuildGroupRow(g.Key, g.ToList(), itemsByVisit, relatedKeySelector: v => v.CustomerName))
                .OrderByDescending(x => x.VisitCount)
                .ToList();

            var byCustomer = visits
                .Where(v => !string.IsNullOrWhiteSpace(v.CustomerName))
                .GroupBy(v => v.CustomerName!)
                .Select(g => BuildGroupRow(g.Key, g.ToList(), itemsByVisit, relatedKeySelector: v => v.SalesEmployeeBP))
                .OrderByDescending(x => x.VisitCount)
                .ToList();

            // ── รายละเอียดการเข้าเยี่ยมแต่ละครั้ง ─────────────────────────────────
            var visitRows = visits.Select(v =>
            {
                var visitItems = itemsByVisit.TryGetValue(v.VisitReportId, out var list) ? list : new List<MGT_VisitItem>();
                int? durationMinutes = (v.StartDateTime.HasValue && v.EndDateTime.HasValue && v.EndDateTime > v.StartDateTime)
                    ? (int)Math.Round((v.EndDateTime.Value - v.StartDateTime.Value).TotalMinutes)
                    : null;

                var contactPersons = visitItems
                    .Select(i => i.ContactPerson)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                return new VisitRowDto
                {
                    VisitReportId = v.VisitReportId,
                    VisitNumber = v.VisitNumber ?? "",
                    VisitDate = v.VisitDate,
                    SalesEmployeeBP = v.SalesEmployeeBP ?? "",
                    CustomerName = v.CustomerName ?? "",
                    StartDateTime = v.StartDateTime,
                    EndDateTime = v.EndDateTime,
                    DurationMinutes = durationMinutes,
                    Status = v.EndDateTime.HasValue ? "Completed" : "In Progress",
                    ProductCount = visitItems.Count,
                    CompetitorCount = visitItems.Count(HasCompetitor),
                    ContactPersons = string.Join(", ", contactPersons),
                    DescriptionRemark = v.DescriptionRemark ?? "",
                    Items = visitItems.Select(i => new VisitItemDetailDto
                    {
                        MaterialName = i.MaterialName,
                        CompetitorName = i.CompetitorName,
                        CompetitorSupplier = i.CompetitorSupplier,
                        ContactPerson = i.ContactPerson,
                        Position = i.Position,
                        Price = i.Price,
                        Consumption = i.Consumption,
                        MakerOrigin = i.MakerOrigin,
                        Application = i.Application,
                        Potential = i.Potential,
                        StatusNote = i.StatusNote
                    }).ToList()
                };
            }).ToList();

            var availableSalesEmployees = await GetDistinctVisitFieldAsync(filter, v => v.SalesEmployeeBP, ct);
            var availableCustomers = await GetDistinctVisitFieldAsync(filter, v => v.CustomerName, ct);

            return new VisitDailyReportDto
            {
                Kpi = kpi,
                Trend = trend,
                BySalesperson = bySalesperson,
                ByCustomer = byCustomer,
                Visits = visitRows,
                AvailableSalesEmployees = availableSalesEmployees,
                AvailableCustomers = availableCustomers
            };
        }

        private static bool HasCompetitor(MGT_VisitItem item) =>
            !string.IsNullOrWhiteSpace(item.CompetitorName) || !string.IsNullOrWhiteSpace(item.CompetitorSupplier);

        // ★ "Potential" เป็น free-text ที่ Sales กรอกเอง (เช่น "Yes") ไม่ใช่ boolean มาตรฐาน
        // นับเป็น "มีศักยภาพ" ถ้ามีค่าและไม่ใช่ค่าที่แปลว่าไม่มี ("No"/"-")
        private static bool IsPotential(string? potential)
        {
            if (string.IsNullOrWhiteSpace(potential)) return false;
            var v = potential.Trim();
            return !v.Equals("no", StringComparison.OrdinalIgnoreCase) && v != "-";
        }

        private static VisitGroupRowDto BuildGroupRow(
            string name, List<MGT_VisitReport> groupVisits,
            Dictionary<string, List<MGT_VisitItem>> itemsByVisit,
            Func<MGT_VisitReport, string?> relatedKeySelector)
        {
            var groupItems = groupVisits
                .SelectMany(v => itemsByVisit.TryGetValue(v.VisitReportId, out var list) ? list : new List<MGT_VisitItem>())
                .ToList();

            return new VisitGroupRowDto
            {
                GroupName = name,
                VisitCount = groupVisits.Count,
                RelatedCount = groupVisits.Select(relatedKeySelector).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count(),
                ProductCount = groupItems.Count,
                CompetitorCount = groupItems.Count(HasCompetitor)
            };
        }

        private static List<VisitTrendPointDto> BuildTrend(
            List<MGT_VisitReport> visits, Dictionary<string, List<MGT_VisitItem>> itemsByVisit,
            DateTime? dateFrom, DateTime? dateTo)
        {
            var withDate = visits.Where(v => v.VisitDate.HasValue).ToList();
            if (withDate.Count == 0) return new();

            var start = dateFrom ?? withDate.Min(v => v.VisitDate!.Value);
            var end = dateTo ?? withDate.Max(v => v.VisitDate!.Value);

            var cursor = new DateTime(start.Year, start.Month, 1);
            var endMonth = new DateTime(end.Year, end.Month, 1);

            var trend = new List<VisitTrendPointDto>();
            var guard = 0;
            while (cursor <= endMonth && guard < MaxTrendMonths)
            {
                var monthVisits = withDate.Where(v => v.VisitDate!.Value.Year == cursor.Year && v.VisitDate!.Value.Month == cursor.Month).ToList();
                var monthItems = monthVisits.SelectMany(v => itemsByVisit.TryGetValue(v.VisitReportId, out var list) ? list : new List<MGT_VisitItem>());

                trend.Add(new VisitTrendPointDto
                {
                    PeriodLabel = cursor.ToString("MMM yy"),
                    VisitCount = monthVisits.Count,
                    CompetitorFindings = monthItems.Count(HasCompetitor)
                });

                cursor = cursor.AddMonths(1);
                guard++;
            }
            return trend;
        }

        private async Task<List<string>> GetDistinctVisitFieldAsync(
            VisitDailyReportFilter filter, System.Linq.Expressions.Expression<Func<MGT_VisitReport, string?>> keySelector, CancellationToken ct)
        {
            // dropdown scope ตาม SalesGroup เท่านั้น (ไม่ใส่ filter ของตัวเอง) เพื่อให้เห็นตัวเลือกครบหลังเลือกแล้ว
            var baseFilter = new VisitDailyReportFilter { SalesGroup = filter.SalesGroup };
            var query = ApplyScopeFilter(_db.MGT_VisitReport.AsNoTracking(), baseFilter);

            return await query
                .Select(keySelector)
                .Where(v => v != null && v != "")
                .Select(v => v!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync(ct);
        }

        private static IQueryable<MGT_VisitReport> ApplyScopeFilter(IQueryable<MGT_VisitReport> query, VisitDailyReportFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.SalesGroup))
                query = query.Where(x => x.SalesGroup == filter.SalesGroup);

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
                query = query.Where(x => x.CustomerName == filter.CustomerName);

            if (filter.DateFrom.HasValue)
                query = query.Where(x => x.VisitDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(x => x.VisitDate <= filter.DateTo.Value);

            return query;
        }
    }
}
