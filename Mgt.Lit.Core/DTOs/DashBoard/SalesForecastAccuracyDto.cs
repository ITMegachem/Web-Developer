using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Sales Forecast Accuracy Report
    // ★ รายงานนี้อ้างอิงจาก Zoho CRM เท่านั้น ไม่เกี่ยวกับ SAP — กรองเฉพาะ Deal ที่ Forecast_Status = "Yes"
    //   (Sales ทำเครื่องหมายเองว่า deal นี้นับเข้า forecast รอบนี้) แล้ววิเคราะห์ว่าสุดท้าย Won/Lost/ยังเปิดอยู่เท่าไร
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - DateFrom/DateTo กรองตาม ClosingDate (เดือนที่ forecast ไว้ว่าจะปิด)
    public class SalesForecastAccuracyFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerName { get; set; }
        public string? Product { get; set; }          // map -> MGT_DealProduct (subquery)
        public string? IndustryName { get; set; }
    }

    public class ForecastAccuracyKpiDto
    {
        public int TotalForecastedDeals { get; set; }
        public decimal TotalForecastValue { get; set; }

        public int WonDeals { get; set; }
        public decimal WonValue { get; set; }

        public int LostDeals { get; set; }
        public decimal LostValue { get; set; }

        public int PendingDeals { get; set; }
        public decimal PendingValue { get; set; }

        // คำนวณเฉพาะ deal ที่ "ตัดสินผลแล้ว" (Won+Lost) — ไม่รวม Pending เพราะยังไม่รู้ผล
        public decimal? ForecastWinRatePercent { get; set; }        // WonDeals / (WonDeals+LostDeals) * 100
        public decimal? ForecastValueRealizationPercent { get; set; } // WonValue / (WonValue+LostValue) * 100

        public decimal PendingRatePercent { get; set; }             // PendingDeals / TotalForecastedDeals * 100
        public int DecidedDeals { get; set; }                        // WonDeals + LostDeals
    }

    public class ForecastTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public int TotalDeals { get; set; }
        public decimal ForecastValue { get; set; }
        public int WonDeals { get; set; }
        public decimal WonValue { get; set; }
        public int LostDeals { get; set; }
        public decimal LostValue { get; set; }
        public int PendingDeals { get; set; }
        public decimal PendingValue { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (Salesperson / Customer / Industry / Product)
    public class ForecastGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int TotalDeals { get; set; }
        public decimal ForecastValue { get; set; }
        public int WonDeals { get; set; }
        public int LostDeals { get; set; }
        public int PendingDeals { get; set; }
        public decimal? WinRatePercent { get; set; }   // null = ยังไม่มี deal ที่ตัดสินผลแล้วในกลุ่มนี้
    }

    public class ForecastedDealRowDto
    {
        public string DealId { get; set; } = string.Empty;
        public string OpportunityName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string IndustryName { get; set; } = string.Empty;
        public decimal DealAmount { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;   // Won / Lost / Pending
    }

    public class SalesForecastAccuracyDto
    {
        public ForecastAccuracyKpiDto Kpi { get; set; } = new();
        public List<ForecastTrendPointDto> Trend { get; set; } = new();
        public List<ForecastGroupRowDto> BySalesperson { get; set; } = new();
        public List<ForecastGroupRowDto> ByCustomer { get; set; } = new();
        public List<ForecastGroupRowDto> ByIndustry { get; set; } = new();
        public List<ForecastGroupRowDto> ByProduct { get; set; } = new();
        public List<ForecastedDealRowDto> ForecastedDeals { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableCustomers { get; set; } = new();
        public List<string> AvailableProducts { get; set; } = new();
        public List<string> AvailableIndustries { get; set; } = new();
    }
}
