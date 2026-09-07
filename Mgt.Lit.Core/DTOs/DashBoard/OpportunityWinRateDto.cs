using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Opportunity Win Rate Report
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - DateFrom/DateTo กรองตาม "วันที่ปิดจริง" (ActualClosedDate, fallback ClosingDate) ของ Deal ที่ปิดแล้วเท่านั้น
    //   เพราะ Win Rate ต้องนับ "Opportunity ที่ตัดสินผลในช่วงนั้น" ไม่ใช่ Opportunity ที่ถูกสร้างในช่วงนั้น
    public class OpportunityWinRateFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? Product { get; set; }
        public string? IndustryName { get; set; }
    }

    public class WinRateKpiDto
    {
        public int WonOpportunities { get; set; }
        public int LostOpportunities { get; set; }
        public int TotalClosedOpportunities { get; set; }

        // null = N/A (ยังไม่มี Deal ปิดผลเลยในช่วงที่กรอง)
        public decimal? WinRatePercent { get; set; }
        public decimal? LossRatePercent { get; set; }

        public decimal WonValue { get; set; }
        public decimal LostValue { get; set; }
        public decimal? ValueWinRatePercent { get; set; }

        public decimal AvgWonDealSize { get; set; }
        public decimal AvgLostDealSize { get; set; }

        public double? AvgSalesCycleWonDays { get; set; }
        public double? AvgSalesCycleLostDays { get; set; }

        // ── เทียบกับช่วงก่อนหน้า (ความยาวช่วงเวลาเท่ากัน ต่อท้ายกันทันที) ─────────
        public int WonVsPreviousCount { get; set; }
        public int ClosedVsPreviousCount { get; set; }
        public decimal? WinRateVsPreviousPoint { get; set; }
        public decimal? ValueWinRateVsPreviousPoint { get; set; }
        public decimal? AvgWonDealSizeVsPreviousPercent { get; set; }
    }

    public class WinRateTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;   // เช่น "Jun 23"
        public int WonDeals { get; set; }
        public int LostDeals { get; set; }
        public int ClosedDeals { get; set; }
        public decimal? WinRatePercent { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (Salesperson / Product / Industry)
    public class WinRateGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int WonDeals { get; set; }
        public int LostDeals { get; set; }
        public int ClosedDeals { get; set; }
        public decimal? WinRatePercent { get; set; }   // null = N/A (Closed = 0)
    }

    // การกระจายตัวของ Deal ตาม Stage ปัจจุบัน (ไม่มีข้อมูล stage-history จาก Zoho จึงไม่ใช่ funnel win-rate-per-stage จริง)
    public class StageFunnelRowDto
    {
        public string Stage { get; set; } = string.Empty;
        public int DealCount { get; set; }
    }

    public class LostReasonRowDto
    {
        public string Reason { get; set; } = string.Empty;
        public int LostDeals { get; set; }
        public decimal SharePercent { get; set; }
    }

    public class ClosedOpportunityRowDto
    {
        public string DealId { get; set; } = string.Empty;
        public string OpportunityName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string IndustryName { get; set; } = string.Empty;
        public decimal DealAmount { get; set; }
        public DateTime? ClosedDate { get; set; }
        public string Result { get; set; } = string.Empty;      // Won / Lost
        public string? LostReason { get; set; }
    }

    public class WinRateSummaryDto
    {
        public int TotalOpportunities { get; set; }
        public int OpenOpportunities { get; set; }
        public int ClosedOpportunities { get; set; }
        public int WonOpportunities { get; set; }
        public int LostOpportunities { get; set; }
        public decimal WonValue { get; set; }
        public decimal LostValue { get; set; }
        public decimal TotalClosedValue { get; set; }
        public decimal? ValueWinRatePercent { get; set; }
    }

    public class OpportunityWinRateDto
    {
        public WinRateKpiDto Kpi { get; set; } = new();
        public List<WinRateTrendPointDto> Trend { get; set; } = new();
        public List<WinRateGroupRowDto> BySalesperson { get; set; } = new();
        public List<WinRateGroupRowDto> ByProduct { get; set; } = new();
        public List<WinRateGroupRowDto> ByIndustry { get; set; } = new();
        public List<StageFunnelRowDto> PipelineByStage { get; set; } = new();
        public List<LostReasonRowDto> LostReasons { get; set; } = new();
        public List<ClosedOpportunityRowDto> RecentClosedOpportunities { get; set; } = new();
        public WinRateSummaryDto Summary { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableProducts { get; set; } = new();
        public List<string> AvailableIndustries { get; set; } = new();
    }
}
