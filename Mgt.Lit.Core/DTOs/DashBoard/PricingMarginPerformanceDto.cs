using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Pricing & Margin Performance Report (Margin Discipline)
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security) เหมือนรายงานอื่น
    // - MonthFrom/MonthTo: ตัวกรองช่วงเดือน (Period) — ไม่ใส่ทั้งคู่ = ทั้งปี
    public class PricingMarginPerformanceFilter
    {
        public int? Year { get; set; }
        public int? MonthFrom { get; set; }
        public int? MonthTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerGroup { get; set; }       // map -> AffiliateCustomerName
        public string? ProductCategory { get; set; }     // map -> MaterialGroupName
        public decimal TargetMarginPercent { get; set; } = 30m;
    }

    // ตัวเลข KPI พร้อม %เทียบปีก่อนหน้า (ช่วงเวลาเดียวกัน) — null เมื่อไม่ได้เลือกปี หรือปีก่อนหน้าไม่มีข้อมูล
    public class KpiMetricDto
    {
        public decimal Value { get; set; }
        public decimal? VsLastYearPercent { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (Salesperson / Customer / Product Category) เทียบกับ Target Margin
    public class MarginGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal CostAmount { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal MarginPercent { get; set; }
        public decimal DiffFromTargetPercent { get; set; }
        public bool MeetsTarget { get; set; }
    }

    public class MarginTrendPointDto
    {
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal MarginPercent { get; set; }
    }

    // 1 แท่งของ waterfall "Sales Bridge" — หน่วยเป็น percentage point ของ Gross Margin
    // IsTotal = true สำหรับแท่ง LY Margin / CY Margin (แท่งเริ่ม-จบ) ที่เหลือเป็นแท่ง delta
    public class MarginBridgeStepDto
    {
        public string Label { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public bool IsTotal { get; set; }
    }

    public class ShareRowDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal SharePercent { get; set; }
    }

    // 1 แถว = 1 SKU (Material) สำหรับตาราง Product Performance Overview
    public class ProductPerformanceRowDto
    {
        public string Material { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string ProductCategory { get; set; } = string.Empty;   // MaterialGroupName
        public string ProductGroup { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;   // salesperson จากรายการขายล่าสุดของ material นี้
        public decimal NetAmount { get; set; }
        public decimal SalesVolume { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal AverageSellingPrice { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal MarginPercent { get; set; }

        // เทียบ Margin% ปีเดียวกันของปีก่อนหน้า (percentage point) — null ถ้าไม่ได้เลือกปี หรือ SKU นี้ไม่มีขายปีก่อน
        public decimal? VsLastYearMarginPointDiff { get; set; }
    }

    public class PricingMarginPerformanceDto
    {
        public decimal TargetMarginPercent { get; set; }

        // ── KPI row ──────────────────────────────────────────────────────────
        public KpiMetricDto NetSales { get; set; } = new();
        public KpiMetricDto GrossProfit { get; set; } = new();
        public KpiMetricDto AverageSellingPrice { get; set; } = new();
        public KpiMetricDto SalesVolume { get; set; } = new();
        public KpiMetricDto CustomerCount { get; set; } = new();

        public decimal GrossMarginPercent { get; set; }
        public decimal? GrossMarginVsLastYearPoint { get; set; }   // pp เทียบปีก่อน (มีค่าจริง)
        public decimal GrossMarginVsTargetPoint { get; set; }      // pp เทียบ Target ที่ตั้งไว้ (คำนวณได้เสมอ)

        // ── Compliance summary (อิงจากกลุ่มลูกค้าทั้งหมด) ───────────────────
        public int GroupsTotal { get; set; }
        public int GroupsMeetingTarget { get; set; }
        public int GroupsBelowTarget { get; set; }
        public decimal ComplianceRatePercent { get; set; }

        public List<MarginTrendPointDto> MarginTrend { get; set; } = new();
        public List<MarginBridgeStepDto> MarginBridge { get; set; } = new();
        public List<ShareRowDto> SalesByBu { get; set; } = new();

        public List<MarginGroupRowDto> BySalesperson { get; set; } = new();
        public List<MarginGroupRowDto> TopCustomers { get; set; } = new();
        public List<MarginGroupRowDto> BottomCustomers { get; set; } = new();
        public List<MarginGroupRowDto> ByProductCategory { get; set; } = new();
        public List<ProductPerformanceRowDto> ProductPerformance { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU + Year) ─────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableCustomerGroups { get; set; } = new();
        public List<string> AvailableProductCategories { get; set; } = new();
    }
}
