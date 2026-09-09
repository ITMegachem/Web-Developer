using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Average Days to Close Report
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - DateFrom/DateTo กรองตาม "วันที่ปิด Closed Won จริง" (ActualClosedDate, fallback ClosingDate)
    // - CustomerName: mapped จาก dropdown "Customer Group" ใน mockup — MGT_Deal (จาก Zoho) ไม่มี field
    //   segment/group ของลูกค้าแยกต่างหาก จึงใช้ชื่อลูกค้าโดยตรงเป็นตัวกรองนี้
    public class AverageDaysToCloseFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? Product { get; set; }
        public string? IndustryName { get; set; }
        public string? CustomerName { get; set; }
    }

    public class DaysToCloseKpiDto
    {
        public double? AvgDaysToClose { get; set; }
        public double? MedianDaysToClose { get; set; }
        public int? MinDaysToClose { get; set; }
        public int? MaxDaysToClose { get; set; }
        public int ClosedWonDeals { get; set; }
        public int DealsOver90Days { get; set; }
        public decimal DealsOver90DaysPercent { get; set; }

        public string? FastestCustomerName { get; set; }
        public int? FastestCustomerDays { get; set; }
        public string? SlowestCustomerName { get; set; }
        public int? SlowestCustomerDays { get; set; }
        public string? LongestSalespersonName { get; set; }
        public double? LongestSalespersonAvgDays { get; set; }

        // ★ Variance = Closing Date (วันที่คาดว่าจะปิด) - Actual Closing Date (วันที่ปิดจริง) เฉลี่ยเป็นวัน
        // บวก = ปิดเร็วกว่าที่วางแผนไว้, ลบ = ปิดช้ากว่าแผน — คำนวณเฉพาะ Deal ที่มี Actual Closing Date จริง (ไม่ fallback)
        public double? AvgVarianceDays { get; set; }

        // ── เทียบกับช่วงก่อนหน้า (ความยาวช่วงเวลาเท่ากัน ต่อท้ายกันทันที ก่อน DateFrom) ─────
        // ทุกตัวเป็น % เปลี่ยนแปลงเทียบช่วงก่อนหน้า (Current-Previous)/Previous*100 ยกเว้นที่ระบุหน่วยไว้ชัดเจน
        public double? AvgDaysChangeVsPrevious { get; set; }        // ผลต่างเป็น "วัน" (ติดลบ = เร็วขึ้น) — ตามสเปคข้อ 16
        public decimal? AvgDaysImprovementPercent { get; set; }     // (previous-current)/previous*100 (บวก = ดีขึ้น) — ตามสเปคข้อ 16
        public int ClosedWonDealsChangeVsPrevious { get; set; }     // ผลต่างเป็นจำนวน Deal
        public decimal? ClosedWonDealsChangePercent { get; set; }
        public decimal? DealsOver90DaysRateChangePercent { get; set; }
    }

    public class DaysToCloseTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public int ClosedWonDeals { get; set; }
        public double? AvgDaysToClose { get; set; }
    }

    // เร็ว (≤30) / ปานกลาง (31-70) / เริ่มนาน (71-90) / นาน (>90)
    public class DaysToCloseStatusRowDto
    {
        public string Status { get; set; } = string.Empty;
        public int DealCount { get; set; }
        public decimal SharePercent { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (Industry / Product / Salesperson / Customer)
    public class DaysToCloseGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int ClosedWonDeals { get; set; }
        public double AvgDays { get; set; }
        public int MinDays { get; set; }
        public int MaxDays { get; set; }
        public string Status { get; set; } = string.Empty;   // bucket ของ AvgDays
    }

    public class ClosedWonOpportunityRowDto
    {
        public string DealId { get; set; } = string.Empty;
        public string OpportunityName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string IndustryName { get; set; } = string.Empty;
        public decimal DealAmount { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ClosedWonDate { get; set; }
        public int? DaysToClose { get; set; }
        public string Status { get; set; } = string.Empty;

        // ★ Variance = Closing Date - Actual Closing Date (null ถ้าไม่มี Actual Closing Date จริง)
        public int? VarianceDays { get; set; }
    }

    public class AverageDaysToCloseDto
    {
        public DaysToCloseKpiDto Kpi { get; set; } = new();
        public List<DaysToCloseTrendPointDto> Trend { get; set; } = new();
        public List<DaysToCloseStatusRowDto> ByStatus { get; set; } = new();
        public List<DaysToCloseGroupRowDto> ByIndustry { get; set; } = new();
        public List<DaysToCloseGroupRowDto> ByProduct { get; set; } = new();
        public List<DaysToCloseGroupRowDto> BySalesperson { get; set; } = new();
        public List<DaysToCloseGroupRowDto> ByCustomer { get; set; } = new();
        public List<ClosedWonOpportunityRowDto> RecentClosedWonOpportunities { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableProducts { get; set; } = new();
        public List<string> AvailableIndustries { get; set; } = new();
        public List<string> AvailableCustomers { get; set; } = new();
    }
}
