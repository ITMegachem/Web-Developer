using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Sales Productivity Report
    // ★ ต่างจาก Opportunity Win Rate Report ตรงที่รายงานนี้กรองตาม "วันที่สร้าง Opportunity" (CreatedDate) เสมอ
    //   ไม่ใช่วันที่ปิด — เพื่อวิเคราะห์เป็น cohort ว่า Opportunity ที่ Sales สร้างในช่วงนั้นๆ สุดท้ายไปทางไหนบ้าง
    //   (Won/Lost/ยังเปิดอยู่) ไม่ว่าจะปิดจริงเมื่อไหร่ก็ตาม
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    public class SalesProductivityFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? Stage { get; set; }
        public string? Product { get; set; }
        public string? CustomerName { get; set; }
    }

    public class SalesProductivityKpiDto
    {
        public int TotalOpportunities { get; set; }
        public decimal? TotalOpportunitiesChangePercent { get; set; }   // vs previous period เท่ากันความยาว

        // ★ นับจาก Sales ที่ปรากฏใน Opportunity ของช่วงที่กรองเท่านั้น (ไม่ใช่ Sales Master ทั้งหมด — ดู note ในหน้า Razor)
        public int ActiveSalespersons { get; set; }
        public double AvgOpportunityPerSalesperson { get; set; }

        public int QualifiedOpportunities { get; set; }
        public decimal QualifiedOpportunityRatePercent { get; set; }

        public int WonOpportunities { get; set; }
        public int LostOpportunities { get; set; }
        public int ClosedOpportunities { get; set; }
        public decimal? OverallWinRatePercent { get; set; }         // null = N/A (ยังไม่มี deal ปิดผลเลย)
        public decimal ConversionRatePercent { get; set; }           // Won / Total Opportunities * 100

        public decimal RevenueClosedWon { get; set; }
        public decimal RevenuePerOpportunity { get; set; }
        public decimal AvgWonDealSize { get; set; }
        public decimal PipelineValue { get; set; }                    // มูลค่า Opportunity ที่ยังเปิดอยู่ในกลุ่มที่สร้างช่วงนี้
    }

    public class SalesProductivityTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public int NewOpportunities { get; set; }
        public int WonDeals { get; set; }
        public int LostDeals { get; set; }
    }

    // 1 แถว = 1 Salesperson (ตารางหลักของรายงาน)
    public class SalesProductivityRowDto
    {
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public int Opportunities { get; set; }
        public int Qualified { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public int Closed { get; set; }
        public decimal? WinRatePercent { get; set; }        // null = N/A (ยังไม่มี deal ปิดผลเลย)
        public decimal PipelineValue { get; set; }
        public decimal Revenue { get; set; }
        public decimal RevenuePerOpportunity { get; set; }
        public decimal ProductivityIndexPercent { get; set; } // Opportunities ÷ ค่าเฉลี่ยทีม × 100

        // Excellent / Good / Watch / Needs Improvement / N/A — ดู business rule ใน service (ประเมินหลายมิติ ไม่ใช้ Win Rate เดี่ยวๆ)
        public string Status { get; set; } = string.Empty;
    }

    // สินค้าที่ถูกดึงมาจาก Zoho field "Material_Group" บน Deal_Items — จริงๆ คือรหัสผู้ผลิต/ซัพพลายเออร์ ไม่ใช่หมวดสินค้าเคมี
    // (Additives/Fillers/Stabilizers ตาม mockup) จึงใช้ "Product" (ชื่อสินค้า) แทนเพื่อไม่ให้ตีความข้อมูลผิด
    public class ProductGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int OpportunityCount { get; set; }
    }

    public class PipelineDealRowDto
    {
        public string DealId { get; set; } = string.Empty;
        public string OpportunityName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public decimal DealAmount { get; set; }
        public DateTime? ClosingDate { get; set; }
    }

    public class SalesProductivityDto
    {
        public SalesProductivityKpiDto Kpi { get; set; } = new();
        public List<SalesProductivityTrendPointDto> Trend { get; set; } = new();
        public List<StageFunnelRowDto> PipelineByStage { get; set; } = new();
        public List<SalesProductivityRowDto> BySalesperson { get; set; } = new();
        public List<LostReasonRowDto> LostReasons { get; set; } = new();
        public List<ClosedOpportunityRowDto> RecentClosedWon { get; set; } = new();
        public List<PipelineDealRowDto> OpenPipelineTop { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableStages { get; set; } = new();
        public List<string> AvailableProducts { get; set; } = new();
        public List<string> AvailableCustomers { get; set; } = new();
    }
}
