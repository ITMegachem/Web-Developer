using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Cross-Sell & Upsell Gains Report
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - DateFrom/DateTo = Current Period; Baseline/Previous Period = ช่วงเวลาเดียวกัน "ปีก่อนหน้า" เสมอ (YoY)
    //   ตรงกับที่ mockup โชว์ "vs Jan-May 2023" ไม่ใช่ช่วงก่อนหน้าติดกัน
    public class CrossSellUpsellGainsFilter
    {
        public System.DateTime? DateFrom { get; set; }
        public System.DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? ProductGroup { get; set; }        // map -> MaterialGroupName
        public string? IndustryName { get; set; }
        public string? CustomerGroup { get; set; }        // map -> AffiliateCustomerName
    }

    public class CrossSellUpsellKpiDto
    {
        public decimal ExistingCustomerRevenue { get; set; }
        public decimal? ExistingCustomerRevenueYoYPercent { get; set; }

        public decimal CrossSellRevenue { get; set; }
        public decimal? CrossSellRevenueYoYPercent { get; set; }

        public decimal UpsellRevenue { get; set; }
        public decimal? UpsellRevenueYoYPercent { get; set; }

        public decimal ExpansionRevenue { get; set; }             // CrossSellRevenue + UpsellRevenue
        public decimal? ExpansionRevenueYoYPercent { get; set; }

        public int ActiveCustomers { get; set; }                  // Existing customers ที่มี revenue > 0 ใน Current Period
        public int Buy1ProductGroupCustomers { get; set; }

        public int ExistingCustomers { get; set; }                // ฐาน Existing ทั้งหมด (FirstPurchase < DateFrom)
        public int CrossSellCustomers { get; set; }
        public decimal CrossSellRatePercent { get; set; }
        public int? CrossSellCustomersYoYChange { get; set; }

        public int UpsellCustomers { get; set; }
        public decimal UpsellRatePercent { get; set; }
        public int? UpsellCustomersYoYChange { get; set; }

        public double AvgProductGroupsPerCustomer { get; set; }
        public double? AvgProductGroupsPerCustomerYoYChange { get; set; }

        public decimal AvgRevenuePerCustomer { get; set; }
        public decimal? AvgRevenuePerCustomerYoYPercent { get; set; }

        public decimal ExpansionRevenueRatePercent { get; set; }   // ExpansionRevenue / ExistingCustomerRevenue * 100
        public decimal? ExpansionRevenueRateYoYPoint { get; set; }

        public decimal CustomerRetentionRatePercent { get; set; } // ActiveCustomers / ExistingCustomers * 100
        public decimal? CustomerRetentionRateYoYPoint { get; set; }
    }

    public class ExpansionTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public decimal BaseRevenue { get; set; }          // รายได้จาก Product Group เดิมที่ไม่ได้เติบโต (retained)
        public decimal CrossSellRevenue { get; set; }
        public decimal UpsellRevenue { get; set; }
        public decimal ExpansionRevenueRatePercent { get; set; }
    }

    public class CustomerGrowthRowDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public decimal CurrentRevenue { get; set; }
        public decimal PreviousRevenue { get; set; }
        public decimal? GrowthPercent { get; set; }
    }

    public class TopCustomerExpansionRowDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;   // ชื่อ salesperson จากรายการขายล่าสุดของ customer นี้ (ดู pattern เดียวกันใน CustomerChurnAnalysisService)
        public decimal ExistingRevenue { get; set; }
        public decimal CrossSellRevenue { get; set; }
        public decimal UpsellRevenue { get; set; }
        public decimal ExpansionRevenue { get; set; }
        public decimal? YoYGrowthPercent { get; set; }
    }

    public class ProductGroupCountRowDto
    {
        public string GroupCountLabel { get; set; } = string.Empty;   // "1 Group" / "2 Groups" / "3 Groups" / "4+ Groups"
        public int CustomerCount { get; set; }
        public decimal SharePercent { get; set; }
        public string Priority { get; set; } = string.Empty;          // High / Medium / Develop

        // ★ รายชื่อลูกค้าในกลุ่มนี้ — สำหรับ popup รายละเอียดตอน Click แถวในตาราง
        public List<ProductGroupCountCustomerDto> Customers { get; set; } = new();
    }

    public class ProductGroupCountCustomerDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public int GroupCount { get; set; }
        public decimal Revenue { get; set; }
        public List<string> ProductGroups { get; set; } = new();
    }

    public class CrossSellByProductGroupRowDto
    {
        public int Rank { get; set; }
        public string ProductGroup { get; set; } = string.Empty;
        public decimal CrossSellRevenue { get; set; }
        public int CrossSellCustomers { get; set; }
        public decimal SharePercent { get; set; }

        // ★ รายชื่อลูกค้าที่ Cross-Sell กลุ่มสินค้านี้ — สำหรับ popup รายละเอียดตอน Click แถวในตาราง
        public List<ProductGroupRevenueCustomerDto> Customers { get; set; } = new();
    }

    public class ProductGroupRevenueCustomerDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public class UpsellByProductGroupRowDto
    {
        public string ProductGroup { get; set; } = string.Empty;
        public decimal PreviousRevenue { get; set; }
        public decimal CurrentRevenue { get; set; }
        public decimal GainRevenue { get; set; }
        public decimal? GrowthPercent { get; set; }
        public int UpsellCustomers { get; set; }
    }

    // รายการลูกค้าที่มี Product Group น้อย + สินค้าที่บริษัทขายอยู่แต่ลูกค้ายังไม่เคยซื้อ
    // ⚠️ เป็นการเทียบแบบ "ไม่เช็ค Eligibility" (ยังไม่มีข้อมูลว่าลูกค้า/อุตสาหกรรมไหนซื้อสินค้ากลุ่มใดได้บ้าง)
    //   ใช้เป็น "รายชื่อผู้สมัครเบื้องต้น" ให้ Sales ไปกลั่นกรองต่อ ไม่ใช่ Opportunity ที่ยืนยันแล้ว
    public class CrossSellOpportunityRowDto
    {
        public string CustomerName { get; set; } = string.Empty;
        public string IndustryName { get; set; } = string.Empty;
        public List<string> CurrentProductGroups { get; set; } = new();
        public List<string> UnpurchasedProductGroups { get; set; } = new();
        public decimal CurrentRevenue { get; set; }
    }

    public class CrossSellUpsellGainsDto
    {
        public CrossSellUpsellKpiDto Kpi { get; set; } = new();
        public List<ExpansionTrendPointDto> Trend { get; set; } = new();
        public List<CustomerGrowthRowDto> CustomerGrowthYoY { get; set; } = new();
        public List<TopCustomerExpansionRowDto> TopCustomers { get; set; } = new();
        public List<ProductGroupCountRowDto> ByProductGroupCount { get; set; } = new();
        public List<CrossSellByProductGroupRowDto> CrossSellByProductGroup { get; set; } = new();
        public List<UpsellByProductGroupRowDto> UpsellByProductGroup { get; set; } = new();
        public List<CrossSellOpportunityRowDto> CrossSellOpportunities { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableProductGroups { get; set; } = new();
        public List<string> AvailableIndustries { get; set; } = new();
        public List<string> AvailableCustomerGroups { get; set; } = new();
    }
}
