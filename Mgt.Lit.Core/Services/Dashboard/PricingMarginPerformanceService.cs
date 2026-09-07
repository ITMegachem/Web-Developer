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
    // MGT_Sale เป็น VIEW ที่ index ไม่ได้ (join ผ่าน base table อีกหลายชั้น รวมถึง Op_SalesOrder ที่ report อื่นก็ใช้อยู่
    // จึงตัดสินใจไม่แตะ/ไม่ทำ index ที่ต้นทาง — ดู comment ใน CrossSellUpsellGainsService ประกอบ) ทุก query ที่ยิงไปที่ view นี้
    // มีต้นทุนคงที่สูงจากการ evaluate join chain ทั้งชุด แทบไม่ขึ้นกับว่า filter/group ให้แคบแค่ไหน — เดิม service นี้ยิง
    // query แยกไปที่ MGT_Sale ถึง ~11-16 ครั้งต่อการโหลด 1 ครั้ง (Totals/GroupBy/Trend/Bridge/ProductPerformance/Dropdown
    // ต่างคนต่างยิงคิวรีของตัวเอง ทั้งที่หลายตัวใช้ raw data ชุดเดียวกัน แค่ group คนละแบบ และ LY MaterialTotals ถูกคำนวณซ้ำ
    // 2 รอบใน Bridge กับ ProductPerformance) ปรับให้เหลือ query หลักแค่ 2-3 ครั้ง โดยดึง raw rows ของ "ทั้งปี" (ตาม
    // ApplyCommonFilter ไม่รวม MonthFrom/MonthTo) มาครั้งเดียวต่อปี (CY และ LY ถ้าเลือกปีไว้) แล้ว group/filter เดือนต่อ
    // ในหน่วยความจำแทนทั้งหมด — เก็บเฉพาะ Month (ไม่ใช่วันที่เต็ม) เพราะ filter ของรายงานนี้ละเอียดสุดแค่ระดับเดือน
    public class PricingMarginPerformanceService : IPricingMarginPerformanceService
    {
        private readonly AppDbContext _db;

        public PricingMarginPerformanceService(AppDbContext db) => _db = db;

        private sealed record RawSaleRow(
            string? SalesEmployeeBP, string? CustomerFullName, string? MaterialGroupName, string? Material, string? MaterialName,
            string? ProductGroup, string? Unit, string? SalesGroup, int? Month,
            decimal NetAmount, decimal CostAmount, decimal GrossProfit, decimal Quantity);

        public async Task<PricingMarginPerformanceDto> GetAsync(PricingMarginPerformanceFilter filter, CancellationToken ct = default)
        {
            // สำคัญ: EF Core DbContext ไม่ thread-safe ห้ามยิงหลาย query พร้อมกันด้วย Task.WhenAll
            var target = filter.TargetMarginPercent;

            // ── ดึง raw rows ของทั้งปี (CY) มาครั้งเดียว — ใช้ ApplyCommonFilter (ไม่รวม MonthFrom/MonthTo) เพราะ
            //   MarginTrend ต้องเห็นครบ Jan-Dec เสมอ ส่วนตัวคำนวณอื่นที่ต้องกรองช่วงเดือน จะกรอง Month ต่อในหน่วยความจำ ──
            var cyRawFullYear = await GetRawRowsAsync(ApplyCommonFilter(_db.MGT_Sale.AsNoTracking(), filter), ct);
            var cyRaw = FilterByMonthRange(cyRawFullYear, filter.MonthFrom, filter.MonthTo).ToList();

            var cyTotals = ComputeTotals(cyRaw);
            var cyMarginPercent = cyTotals.NetAmount == 0 ? 0 : Math.Round(cyTotals.GrossProfit / cyTotals.NetAmount * 100, 2);
            var cyAsp = cyTotals.Qty == 0 ? 0 : Math.Round(cyTotals.NetAmount / cyTotals.Qty, 2);

            // ── ตัวเลข LY ช่วงเวลาเดียวกัน (ใช้เทียบ vs LY) — คำนวณได้เฉพาะตอนเลือกปีแล้วเท่านั้น ──
            List<RawSaleRow> lyRaw = new();
            Totals? lyTotals = null;
            if (filter.Year.HasValue)
            {
                var lyFilter = CloneForYear(filter, filter.Year.Value - 1);
                var lyRawFullYear = await GetRawRowsAsync(ApplyCommonFilter(_db.MGT_Sale.AsNoTracking(), lyFilter), ct);
                lyRaw = FilterByMonthRange(lyRawFullYear, filter.MonthFrom, filter.MonthTo).ToList();
                lyTotals = ComputeTotals(lyRaw);
            }
            decimal? lyMarginPercent = (lyTotals is { NetAmount: not 0 })
                ? Math.Round(lyTotals.Value.GrossProfit / lyTotals.Value.NetAmount * 100, 2)
                : null;
            decimal? lyAsp = (lyTotals is { Qty: not 0 })
                ? Math.Round(lyTotals.Value.NetAmount / lyTotals.Value.Qty, 2)
                : null;

            static decimal? PctChange(decimal cy, decimal? ly) =>
                (ly.HasValue && ly.Value != 0) ? Math.Round((cy - ly.Value) / Math.Abs(ly.Value) * 100, 2) : null;

            // ── Breakdown ตามกลุ่ม (เรียง Margin ต่ำ -> สูง เสมอ) — group จาก cyRaw ที่ดึงมาแล้ว ไม่ยิง query เพิ่ม ──
            var bySalesperson = GroupByField(cyRaw, x => x.SalesEmployeeBP, target);
            var byCustomerFull = GroupByField(cyRaw, x => x.CustomerFullName, target);
            var byProductCategory = GroupByField(cyRaw, x => x.MaterialGroupName, target);

            var groupsTotal = byCustomerFull.Count;
            var groupsMeetingTarget = byCustomerFull.Count(r => r.MeetsTarget);

            var topCustomers = byCustomerFull.AsEnumerable().Reverse().Take(5).ToList();       // margin สูงสุดก่อน
            var bottomCustomers = byCustomerFull.Take(5).ToList();                              // margin ต่ำสุดก่อน (เรียงมาแล้ว)

            var marginTrend = BuildMarginTrend(cyRawFullYear, await GetMonthsAsync(filter, ct));

            // ── Material-level totals — คำนวณครั้งเดียวจาก cyRaw/lyRaw ที่มีอยู่แล้ว ใช้ทั้งใน Bridge และ Product Performance ──
            var cyMaterialTotals = BuildMaterialTotals(cyRaw);
            var lyMaterialTotals = filter.Year.HasValue ? BuildMaterialTotals(lyRaw) : new List<MaterialTotals>();

            var marginBridge = BuildMarginBridge(filter, cyMaterialTotals, lyMaterialTotals);
            var salesByBu = BuildSalesByBu(cyRaw);

            Dictionary<string, decimal>? lyMarginByMaterial = filter.Year.HasValue
                ? lyMaterialTotals.Where(x => x.NetAmount != 0)
                    .GroupBy(x => x.Material)
                    .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.NetAmount - x.CostAmount) / g.Sum(x => x.NetAmount) * 100, 2))
                : null;
            var productPerformance = BuildProductPerformance(cyRaw, lyMarginByMaterial);

            var dropdownQuery = ApplyCommonFilter(_db.MGT_Sale.AsNoTracking(), new PricingMarginPerformanceFilter { SalesGroup = filter.SalesGroup, Year = filter.Year });
            var (availableSalesEmployees, availableCustomerGroups, availableProductCategories) = await GetDropdownsAsync(dropdownQuery, ct);

            return new PricingMarginPerformanceDto
            {
                TargetMarginPercent = target,

                NetSales = new KpiMetricDto { Value = cyTotals.NetAmount, VsLastYearPercent = PctChange(cyTotals.NetAmount, lyTotals?.NetAmount) },
                GrossProfit = new KpiMetricDto { Value = cyTotals.GrossProfit, VsLastYearPercent = PctChange(cyTotals.GrossProfit, lyTotals?.GrossProfit) },
                AverageSellingPrice = new KpiMetricDto { Value = cyAsp, VsLastYearPercent = PctChange(cyAsp, lyAsp) },
                SalesVolume = new KpiMetricDto { Value = cyTotals.Qty, VsLastYearPercent = PctChange(cyTotals.Qty, lyTotals?.Qty) },
                CustomerCount = new KpiMetricDto { Value = cyTotals.CustomerCount, VsLastYearPercent = PctChange(cyTotals.CustomerCount, lyTotals?.CustomerCount) },

                GrossMarginPercent = cyMarginPercent,
                GrossMarginVsLastYearPoint = lyMarginPercent.HasValue ? Math.Round(cyMarginPercent - lyMarginPercent.Value, 2) : null,
                GrossMarginVsTargetPoint = Math.Round(cyMarginPercent - target, 2),

                GroupsTotal = groupsTotal,
                GroupsMeetingTarget = groupsMeetingTarget,
                GroupsBelowTarget = groupsTotal - groupsMeetingTarget,
                ComplianceRatePercent = groupsTotal == 0 ? 0 : Math.Round(groupsMeetingTarget * 100m / groupsTotal, 2),

                MarginTrend = marginTrend,
                MarginBridge = marginBridge,
                SalesByBu = salesByBu,

                BySalesperson = bySalesperson,
                TopCustomers = topCustomers,
                BottomCustomers = bottomCustomers,
                ByProductCategory = byProductCategory,
                ProductPerformance = productPerformance,

                AvailableSalesEmployees = availableSalesEmployees,
                AvailableCustomerGroups = availableCustomerGroups,
                AvailableProductCategories = availableProductCategories
            };
        }

        private readonly record struct Totals(decimal NetAmount, decimal GrossProfit, decimal Qty, int CustomerCount);

        // ── query หลักตัวเดียวที่ยิงไปที่ MGT_Sale ต่อปี — ดึงคอลัมน์ที่ทุกส่วนของรายงานต้องใช้มาในครั้งเดียว ──
        private static async Task<List<RawSaleRow>> GetRawRowsAsync(IQueryable<MGT_Sale> query, CancellationToken ct)
        {
            var rows = await query
                .Select(x => new
                {
                    x.SalesEmployeeBP,
                    x.CustomerFullName,
                    x.MaterialGroupName,
                    x.Material,
                    x.MaterialName,
                    x.ProductGroup,
                    x.Unit,
                    x.SalesGroup,
                    Month = x.BillingDocumentDate.HasValue ? (int?)x.BillingDocumentDate.Value.Month : null,
                    NetAmount = x.NetAmount ?? 0,
                    CostAmount = x.CostAmount ?? 0,
                    GrossProfit = x.GrossProfit ?? 0,
                    Quantity = x.Quantity ?? 0
                })
                .ToListAsync(ct);

            return rows.Select(r => new RawSaleRow(
                r.SalesEmployeeBP, r.CustomerFullName, r.MaterialGroupName, r.Material, r.MaterialName,
                r.ProductGroup, r.Unit, r.SalesGroup, r.Month, r.NetAmount, r.CostAmount, r.GrossProfit, (decimal)r.Quantity)).ToList();
        }

        private static IEnumerable<RawSaleRow> FilterByMonthRange(List<RawSaleRow> rows, int? monthFrom, int? monthTo) =>
            rows.Where(r =>
                (!monthFrom.HasValue || (r.Month.HasValue && r.Month.Value >= monthFrom.Value)) &&
                (!monthTo.HasValue || (r.Month.HasValue && r.Month.Value <= monthTo.Value)));

        private static Totals ComputeTotals(List<RawSaleRow> rows) => new(
            rows.Sum(x => x.NetAmount),
            rows.Sum(x => x.GrossProfit),
            rows.Sum(x => x.Quantity),
            rows.Select(x => x.CustomerFullName).Distinct().Count());

        private static List<MarginGroupRowDto> GroupByField(List<RawSaleRow> rows, Func<RawSaleRow, string?> keySelector, decimal target)
        {
            return rows
                .GroupBy(keySelector)
                .Where(g => g.Key != null && g.Key != "")
                .Select(g => new
                {
                    Key = g.Key,
                    NetAmount = g.Sum(x => x.NetAmount),
                    CostAmount = g.Sum(x => x.CostAmount),
                    GrossProfit = g.Sum(x => x.GrossProfit)
                })
                .Select(r =>
                {
                    var margin = r.NetAmount == 0 ? 0 : Math.Round(r.GrossProfit / r.NetAmount * 100, 2);
                    return new MarginGroupRowDto
                    {
                        GroupName = r.Key!,
                        NetAmount = r.NetAmount,
                        CostAmount = r.CostAmount,
                        GrossProfit = r.GrossProfit,
                        MarginPercent = margin,
                        DiffFromTargetPercent = Math.Round(margin - target, 2),
                        MeetsTarget = margin >= target
                    };
                })
                // ★ เรียงจาก Margin ต่ำสุดไปสูงสุด — กลุ่มที่ต้องรีบทบทวนราคา/ส่วนลดขึ้นก่อนเสมอ
                .OrderBy(r => r.MarginPercent)
                .ToList();
        }

        private async Task<List<MsMonth>> GetMonthsAsync(PricingMarginPerformanceFilter filter, CancellationToken ct)
        {
            var selectedYear = filter.Year ?? DateTime.Now.Year;
            var maxMonth = selectedYear == DateTime.Now.Year ? DateTime.Now.Month : 12;

            return await _db.MsMonths.AsNoTracking()
                .Where(m => m.MonthId <= maxMonth)
                .OrderBy(m => m.MonthId)
                .ToListAsync(ct);
        }

        // เทรนด์รายเดือนไม่ผูกกับตัวกรอง Period (MonthFrom/MonthTo) — ต้องเห็นครบ Jan-Dec เสมอ จึงรับ "cyRawFullYear" (ไม่ผ่าน FilterByMonthRange)
        private static List<MarginTrendPointDto> BuildMarginTrend(List<RawSaleRow> cyRawFullYear, List<MsMonth> months)
        {
            var byMonth = cyRawFullYear
                .Where(x => x.Month.HasValue)
                .GroupBy(x => x.Month!.Value)
                .Select(g => new { MonthId = g.Key, NetAmount = g.Sum(x => x.NetAmount), GrossProfit = g.Sum(x => x.GrossProfit) })
                .ToList();

            return months.Select(m =>
            {
                var row = byMonth.FirstOrDefault(r => r.MonthId == m.MonthId);
                var net = row?.NetAmount ?? 0;
                var gp = row?.GrossProfit ?? 0;
                return new MarginTrendPointDto
                {
                    MonthId = m.MonthId,
                    MonthName = m.MonthNameEN,
                    MarginPercent = net == 0 ? 0 : Math.Round(gp / net * 100, 2)
                };
            }).ToList();
        }

        private readonly record struct MaterialTotals(string Material, decimal NetAmount, decimal CostAmount, decimal Qty);

        private static List<MaterialTotals> BuildMaterialTotals(List<RawSaleRow> rows) =>
            rows.Where(x => !string.IsNullOrEmpty(x.Material))
                .GroupBy(x => x.Material!)
                .Select(g => new MaterialTotals(g.Key, g.Sum(x => x.NetAmount), g.Sum(x => x.CostAmount), g.Sum(x => x.Quantity)))
                .ToList();

        // Sales Bridge: decompose การเปลี่ยนแปลง Margin% (LY -> CY) เป็น Price / Volume-Mix / Cost / ส่วนต่างสะสม (pp)
        // จับคู่ระดับ Material (SKU) ระหว่าง CY กับ LY ช่วงเวลาเดียวกัน — คำนวณได้เฉพาะตอนเลือกปีแล้วเท่านั้น
        // หมายเหตุ: "Volume/Mix Impact" รวมทั้งผลของปริมาณขายที่เปลี่ยนและสัดส่วนสินค้าที่เปลี่ยนไว้ด้วยกัน
        // (แยก Mix ออกจาก Volume ล้วนๆ ต้องมีข้อมูลระดับละเอียดกว่านี้ เช่น planned/standard mix)
        private static List<MarginBridgeStepDto> BuildMarginBridge(
            PricingMarginPerformanceFilter filter, List<MaterialTotals> cyRows, List<MaterialTotals> lyRows)
        {
            if (!filter.Year.HasValue) return new List<MarginBridgeStepDto>();

            var cyNetTotal = cyRows.Sum(x => x.NetAmount);
            var lyNetTotal = lyRows.Sum(x => x.NetAmount);
            if (cyNetTotal == 0 || lyNetTotal == 0) return new List<MarginBridgeStepDto>();

            var cyMarginPercent = Math.Round(cyRows.Sum(x => x.NetAmount - x.CostAmount) / cyNetTotal * 100, 2);
            var lyMarginPercent = Math.Round(lyRows.Sum(x => x.NetAmount - x.CostAmount) / lyNetTotal * 100, 2);

            var lyMap = lyRows.ToDictionary(x => x.Material);

            decimal priceImpact = 0, volumeMixImpact = 0, costImpact = 0;
            foreach (var cy in cyRows)
            {
                if (cy.Qty == 0) continue;
                var cyAsp = cy.NetAmount / cy.Qty;
                var cyCostPerUnit = cy.CostAmount / cy.Qty;

                if (lyMap.TryGetValue(cy.Material, out var ly) && ly.Qty != 0)
                {
                    var lyAsp = ly.NetAmount / ly.Qty;
                    var lyCostPerUnit = ly.CostAmount / ly.Qty;
                    var lyMarginPerUnit = lyAsp - lyCostPerUnit;

                    priceImpact += (cyAsp - lyAsp) * cy.Qty;
                    costImpact += -(cyCostPerUnit - lyCostPerUnit) * cy.Qty;
                    volumeMixImpact += (cy.Qty - ly.Qty) * lyMarginPerUnit;
                }
                else
                {
                    // สินค้าที่ไม่มีขายใน LY -> gross profit ทั้งก้อนนับเป็น Volume/Mix Impact (ยอดขายใหม่ที่เพิ่มเข้ามา)
                    volumeMixImpact += cy.NetAmount - cy.CostAmount;
                }
            }

            var priceImpactPp = Math.Round(priceImpact / cyNetTotal * 100, 2);
            var volumeMixImpactPp = Math.Round(volumeMixImpact / cyNetTotal * 100, 2);
            var costImpactPp = Math.Round(costImpact / cyNetTotal * 100, 2);

            // ส่วนต่างสะสม (interaction/rounding) กันผลรวมแท่ง delta ไม่เท่ากับ CY-LY จริง — เป็นเรื่องปกติของ PVM bridge
            var residual = Math.Round((cyMarginPercent - lyMarginPercent) - (priceImpactPp + volumeMixImpactPp + costImpactPp), 2);

            var steps = new List<MarginBridgeStepDto>
            {
                new() { Label = "LY Margin", Value = lyMarginPercent, IsTotal = true },
                new() { Label = "Price Impact", Value = priceImpactPp },
                new() { Label = "Volume/Mix Impact", Value = volumeMixImpactPp },
                new() { Label = "Cost Impact", Value = costImpactPp }
            };
            if (Math.Abs(residual) >= 0.05m)
                steps.Add(new MarginBridgeStepDto { Label = "Other", Value = residual });
            steps.Add(new MarginBridgeStepDto { Label = "CY Margin", Value = cyMarginPercent, IsTotal = true });

            return steps;
        }

        private static List<ShareRowDto> BuildSalesByBu(List<RawSaleRow> rows)
        {
            var grouped = rows
                .Where(x => !string.IsNullOrEmpty(x.SalesGroup))
                .GroupBy(x => x.SalesGroup!)
                .Select(g => new { Name = g.Key, NetAmount = g.Sum(x => x.NetAmount) })
                .OrderByDescending(x => x.NetAmount)
                .ToList();

            var total = grouped.Sum(x => x.NetAmount);
            return grouped.Select(r => new ShareRowDto
            {
                Name = r.Name,
                NetAmount = r.NetAmount,
                SharePercent = total == 0 ? 0 : Math.Round(r.NetAmount / total * 100, 2)
            }).ToList();
        }

        private static List<ProductPerformanceRowDto> BuildProductPerformance(List<RawSaleRow> rows, Dictionary<string, decimal>? lyMarginByMaterial)
        {
            var grouped = rows
                .Where(x => !string.IsNullOrEmpty(x.Material))
                .GroupBy(x => new { x.Material, x.MaterialName, x.MaterialGroupName, x.ProductGroup, x.Unit })
                .Select(g => new
                {
                    g.Key.Material,
                    g.Key.MaterialName,
                    g.Key.MaterialGroupName,
                    g.Key.ProductGroup,
                    g.Key.Unit,
                    NetAmount = g.Sum(x => x.NetAmount),
                    CostAmount = g.Sum(x => x.CostAmount),
                    GrossProfit = g.Sum(x => x.GrossProfit),
                    Qty = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.NetAmount)
                .Take(200)   // กันโหลดหนักถ้ามี SKU เยอะมาก — ตารางเรียงตามยอดขายมากไปน้อยอยู่แล้ว
                .ToList();

            return grouped.Select(r =>
            {
                var qty = r.Qty;
                var margin = r.NetAmount == 0 ? 0 : Math.Round(r.GrossProfit / r.NetAmount * 100, 2);
                decimal? vsLy = (lyMarginByMaterial != null && r.Material != null && lyMarginByMaterial.TryGetValue(r.Material, out var lyMargin))
                    ? Math.Round(margin - lyMargin, 2)
                    : null;

                return new ProductPerformanceRowDto
                {
                    Material = r.Material ?? "",
                    MaterialName = r.MaterialName ?? "",
                    ProductCategory = r.MaterialGroupName ?? "",
                    ProductGroup = r.ProductGroup ?? "",
                    NetAmount = r.NetAmount,
                    SalesVolume = qty,
                    Unit = r.Unit ?? "",
                    AverageSellingPrice = qty == 0 ? 0 : Math.Round(r.NetAmount / qty, 2),
                    CostPerUnit = qty == 0 ? 0 : Math.Round(r.CostAmount / qty, 2),
                    GrossProfit = r.GrossProfit,
                    MarginPercent = margin,
                    VsLastYearMarginPointDiff = vsLy
                };
            }).ToList();
        }

        private static async Task<(List<string> SalesEmployees, List<string> CustomerGroups, List<string> ProductCategories)>
            GetDropdownsAsync(IQueryable<MGT_Sale> dropdownScopeQuery, CancellationToken ct)
        {
            var raw = await dropdownScopeQuery
                .Select(x => new { x.SalesEmployeeBP, x.AffiliateCustomerName, x.MaterialGroupName })
                .Distinct()
                .ToListAsync(ct);

            static List<string> DistinctSorted(IEnumerable<string?> values) =>
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().OrderBy(v => v).ToList();

            return (
                DistinctSorted(raw.Select(x => x.SalesEmployeeBP)),
                DistinctSorted(raw.Select(x => x.AffiliateCustomerName)),
                DistinctSorted(raw.Select(x => x.MaterialGroupName))
            );
        }

        private static PricingMarginPerformanceFilter CloneForYear(PricingMarginPerformanceFilter filter, int year) => new()
        {
            Year = year,
            MonthFrom = filter.MonthFrom,
            MonthTo = filter.MonthTo,
            SalesGroup = filter.SalesGroup,
            SalesEmployeeBP = filter.SalesEmployeeBP,
            CustomerGroup = filter.CustomerGroup,
            ProductCategory = filter.ProductCategory,
            TargetMarginPercent = filter.TargetMarginPercent
        };

        // ★ ใช้กับการดึง raw rows ของ "ทั้งปี" เท่านั้น (ไม่รวม MonthFrom/MonthTo) — กรองช่วงเดือนทำในหน่วยความจำแทน
        // (SalesEmployeeBP/CustomerGroup/ProductCategory ยังคงกรองที่ SQL เหมือนเดิม เพราะเป็น filter ระดับ scope ไม่ใช่ระดับเวลา)
        private static IQueryable<MGT_Sale> ApplyCommonFilter(IQueryable<MGT_Sale> query, PricingMarginPerformanceFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.SalesGroup))
                query = query.Where(x => x.SalesGroup == filter.SalesGroup);

            if (filter.Year.HasValue)
                query = query.Where(x => x.BillingDocumentDate.HasValue
                                       && x.BillingDocumentDate.Value.Year == filter.Year.Value);

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP))
                query = query.Where(x => x.SalesEmployeeBP == filter.SalesEmployeeBP);

            if (!string.IsNullOrWhiteSpace(filter.CustomerGroup))
                query = query.Where(x => x.AffiliateCustomerName == filter.CustomerGroup);

            if (!string.IsNullOrWhiteSpace(filter.ProductCategory))
                query = query.Where(x => x.MaterialGroupName == filter.ProductCategory);

            return query;
        }
    }
}
