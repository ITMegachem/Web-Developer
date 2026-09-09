using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Forecast Monthly Report
    // - Forecast มาจาก Zoho CRM Deal_Items (subform ของ Deals) — field Quantity + Required_Date (เดือนที่ต้องการสินค้า)
    // - On Hand Qty มาจาก SAP (API_MATERIAL_STOCK_SRV) join ด้วย Material Code
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    public class ForecastMonthlyFilter
    {
        public DateTime? FromMonth { get; set; }   // ใช้แค่ปี+เดือน (วันที่ถูก normalize เป็นวันที่ 1 เสมอ)
        public DateTime? ToMonth { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerName { get; set; }
        public string? MaterialGroup { get; set; }
        public string? MaterialCode { get; set; }
        public string? JobDeal { get; set; }       // = Opportunity/Deal Name (MGT_Deal.OpportunityName)
    }

    public class ForecastMonthlyKpiDto
    {
        public decimal TotalForecastQty { get; set; }
        public decimal OnHandQty { get; set; }          // รวมเฉพาะ material ที่ query SAP สำเร็จ
        public decimal ShortageQty { get; set; }         // max(0, TotalForecastQty - OnHandQty)
        public int MaterialsCount { get; set; }
        public int MaterialsWithStockDataCount { get; set; }  // จำนวน material ที่ดึง On Hand จาก SAP ได้สำเร็จ
    }

    public class ForecastMonthlyRowDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialDescription { get; set; } = string.Empty;

        // เรียงตามลำดับเดียวกับ ForecastMonthlyDto.MonthLabels
        public List<decimal> MonthlyQty { get; set; } = new();
        public decimal TotalForecastQty { get; set; }

        // null = ยังไม่สามารถดึง On Hand จาก SAP ได้ (ต่างจาก 0 ที่แปลว่าสต๊อกหมดจริง)
        public decimal? OnHandQty { get; set; }
        public bool IsShortage { get; set; }   // TotalForecastQty > OnHandQty (เฉพาะเมื่อดึง On Hand ได้)
    }

    public class ForecastMonthlyDto
    {
        public DateTime AsOfDate { get; set; }
        public ForecastMonthlyKpiDto Kpi { get; set; } = new();

        // ป้ายกำกับคอลัมน์เดือน เช่น "2026.05" — ใช้คู่กับ ForecastMonthlyRowDto.MonthlyQty (index ตรงกัน)
        public List<string> MonthLabels { get; set; } = new();
        public List<ForecastMonthlyRowDto> Rows { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableCustomers { get; set; } = new();
        public List<string> AvailableMaterialGroups { get; set; } = new();
        public List<string> AvailableMaterials { get; set; } = new();   // แสดงเป็น "Code - Description"
        public List<string> AvailableJobDeals { get; set; } = new();
        public List<string> AvailableSalesEmployees { get; set; } = new();
    }
}
