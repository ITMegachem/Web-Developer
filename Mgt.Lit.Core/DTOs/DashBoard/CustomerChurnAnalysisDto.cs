using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Customer Churn Analysis Report (Reduced Churn Dashboard)
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - Analysis Period ยึดตามปีปฏิทินของ Year เสมอ (Jan 1 - Dec 31) เพราะ cohort Existing/New ต้องนิยามจากปีเต็ม
    public class CustomerChurnAnalysisFilter
    {
        public int? Year { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerGroup { get; set; }     // map -> AffiliateCustomerName
        public string? IndustryName { get; set; }
    }

    public class ChurnKpiDto
    {
        public int ExistingCustomers { get; set; }
        public int ActiveExistingCustomers { get; set; }
        public int LostCustomers { get; set; }
        public int NewCustomers { get; set; }
        public int TotalCustomerBase { get; set; }

        public decimal RetentionRatePercent { get; set; }
        public decimal ChurnRatePercent { get; set; }

        public decimal ExistingRevenue { get; set; }
        public decimal NewRevenue { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal ExistingRevenueSharePercent { get; set; }

        public decimal PreviousExistingRevenue { get; set; }
        public decimal ExistingGrowthPercent { get; set; }

        // Historical (trailing 12M ก่อนวันซื้อครั้งสุดท้ายของแต่ละราย) ของกลุ่ม Lost Customer ทั้งหมด
        public decimal LostRevenue { get; set; }
        public decimal LostGrossProfit { get; set; }
    }

    public class ChurnTrendPointDto
    {
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal ChurnRatePercent { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (เช่น Industry) เทียบ Existing vs Lost
    public class ChurnByGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int ExistingCustomers { get; set; }
        public int LostCustomers { get; set; }
        public decimal ChurnRatePercent { get; set; }
    }

    // 1 แถว = 1 ลูกค้าที่เข้าเกณฑ์ Lost/Churn (Days Since Last Purchase >= 365)
    public class LostCustomerRowDto
    {
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string IndustryName { get; set; } = string.Empty;
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public DateTime LastPurchaseDate { get; set; }
        public int DaysSinceLastPurchase { get; set; }
        public string RiskLevel { get; set; } = string.Empty;     // Watch / High / Critical
        public int RiskScore { get; set; }                        // 0-100, ดูสูตรใน service
        public decimal Last12MRevenue { get; set; }
        public decimal Last12MGrossProfit { get; set; }
        public string Action { get; set; } = string.Empty;        // Monitor / Call / Visit / Win-back Now
    }

    public class RiskSummaryRowDto
    {
        public string RiskLevel { get; set; } = string.Empty;
        public int CustomerCount { get; set; }
        public decimal RevenueAtRisk { get; set; }
        public decimal SharePercent { get; set; }
    }

    public class CustomerChurnAnalysisDto
    {
        public DateTime AsOfDate { get; set; }

        public ChurnKpiDto Kpi { get; set; } = new();
        public List<ChurnTrendPointDto> ChurnTrend { get; set; } = new();
        public List<ChurnByGroupRowDto> ChurnByIndustry { get; set; } = new();
        public List<LostCustomerRowDto> LostCustomers { get; set; } = new();
        public List<RiskSummaryRowDto> RiskSummary { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU + Year) ─────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableCustomerGroups { get; set; } = new();
        public List<string> AvailableIndustries { get; set; } = new();
    }
}
