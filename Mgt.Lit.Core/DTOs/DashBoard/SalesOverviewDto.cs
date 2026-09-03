using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // คำร้องขอกรองข้อมูลของหน้า Sales Overview
    // - Year: ตัวกรองจาก dropdown "Search Year" (กรองควบคู่กับ cross-filter เสมอ)
    // - Bu: server เขียนทับจาก DataScope/Division ของ user (BU security) — client ส่งมาไม่มีผล
    // - SalesEmployeeBP / MaterialGroupName / AffiliateCustomerName / IndustryName / MonthId:
    //   ตัวกรองแบบ "cross-filter" ที่มาจากการคลิกตาราง/กราฟ — ให้มีค่าได้ทีละ 1 ฟิลด์เท่านั้น
    //   (ฝั่ง Front เป็นคนคุมกติกานี้ก่อนยิง request — service แค่ AND ทุกฟิลด์ที่ไม่ null เข้าด้วยกัน)
    public class SalesOverviewFilter
    {
        public int? Year { get; set; }
        public string? Bu { get; set; }
        public string? AffiliateCustomerName { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? MaterialGroupName { get; set; }
        public string? IndustryName { get; set; }
        public int? MonthId { get; set; }
    }

    public class SalesSummaryDto
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalCogs { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal GrossProfitMarginPercent { get; set; }
        public int TotalProduct { get; set; }
        public int TotalCustomer { get; set; }
        public decimal TotalBU1 { get; set; }
        public decimal TotalBU2 { get; set; }
        public decimal TotalBU3 { get; set; }
        public decimal TotalBU4 { get; set; }
        public decimal TotalOther { get; set; }
    }

    // แถวผลลัพธ์ breakdown ของ Salesman / Material Group / Affiliate / Industry (group by Key เดียวกันหมด)
    public class BreakdownRowDto
    {
        public string? Key { get; set; }
        public decimal NetAmount { get; set; }
        public decimal CostAmount { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal MarginPercent { get; set; }
    }

    public class MonthlyRevenueDto
    {
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal GrossProfitMarginPercent { get; set; }
    }

    // Response DTO รวมทุกอย่างที่หน้า Sales Overview ต้องใช้ในการเรียกครั้งเดียว
    public class SalesOverviewDto
    {
        public SalesSummaryDto Summary { get; set; } = new();
        public List<BreakdownRowDto> BySalesman { get; set; } = new();
        public List<BreakdownRowDto> ByMaterialGroup { get; set; } = new();
        public List<BreakdownRowDto> ByAffiliate { get; set; } = new();
        public List<BreakdownRowDto> ByIndustry { get; set; } = new();
        public List<MonthlyRevenueDto> MonthlyRevenue { get; set; } = new();
    }
}
