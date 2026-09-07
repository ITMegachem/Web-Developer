using System;
using System.Collections.Generic;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ตัวกรองของหน้า Visit Daily Report
    // ★ รายงานนี้อ้างอิงจาก Zoho CRM module "Visit_Reports" (+ subform "Visit_Items") เท่านั้น
    // - SalesGroup: server เขียนทับจาก DataScope/Division ของ user เสมอ (BU security)
    // - DateFrom/DateTo กรองตาม VisitDate
    public class VisitDailyReportFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerName { get; set; }
    }

    public class VisitKpiDto
    {
        public int TotalVisits { get; set; }
        public int TotalCustomersVisited { get; set; }     // นับลูกค้าไม่ซ้ำ
        public int TotalSalespeople { get; set; }           // นับ Sales ไม่ซ้ำ

        public int TotalProductItemsDiscussed { get; set; }  // รวมทุกบรรทัดใน Visit_Items ของทุก visit
        public int DistinctProductsDiscussed { get; set; }    // นับสินค้าไม่ซ้ำ
        public double AvgProductsPerVisit { get; set; }

        // ★ นับจาก Visit_Items ที่มี Competitor_Name หรือ Competitor_Supplier กรอกไว้ (ไม่ว่าง)
        public int TotalCompetitorsFound { get; set; }

        // ★ นับจาก Visit_Items ที่ field "Potential" มีค่าและไม่ใช่ "No"/"-" (free-text ที่ Sales กรอกเอง ไม่ใช่ boolean มาตรฐาน)
        public int TotalPotentialItems { get; set; }

        public double? AvgVisitDurationMinutes { get; set; }  // เฉลี่ยเฉพาะ visit ที่มีทั้ง Start และ End time
    }

    public class VisitTrendPointDto
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public int VisitCount { get; set; }
        public int CompetitorFindings { get; set; }
    }

    // 1 แถว = 1 กลุ่ม (Salesperson / Customer)
    public class VisitGroupRowDto
    {
        public string GroupName { get; set; } = string.Empty;
        public int VisitCount { get; set; }
        public int RelatedCount { get; set; }        // Salesperson แถว -> จำนวนลูกค้าไม่ซ้ำ, Customer แถว -> จำนวน Sales ไม่ซ้ำ
        public int ProductCount { get; set; }
        public int CompetitorCount { get; set; }
    }

    public class VisitItemDetailDto
    {
        public string? MaterialName { get; set; }
        public string? CompetitorName { get; set; }
        public string? CompetitorSupplier { get; set; }
        public string? ContactPerson { get; set; }
        public string? Position { get; set; }
        public string? Price { get; set; }
        public string? Consumption { get; set; }
        public string? MakerOrigin { get; set; }
        public string? Application { get; set; }
        public string? Potential { get; set; }
        public string? StatusNote { get; set; }
    }

    public class VisitRowDto
    {
        public string VisitReportId { get; set; } = string.Empty;
        public string VisitNumber { get; set; } = string.Empty;
        public DateTime? VisitDate { get; set; }
        public string SalesEmployeeBP { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public int? DurationMinutes { get; set; }

        // ★ ไม่มี field สถานะตรงๆ บน Zoho — derive จาก EndDateTime มีค่าหรือไม่ (มี = "Completed", ไม่มี = "In Progress")
        public string Status { get; set; } = string.Empty;

        public int ProductCount { get; set; }
        public int CompetitorCount { get; set; }
        public string ContactPersons { get; set; } = string.Empty;  // รวมชื่อผู้ติดต่อไม่ซ้ำ คั่นด้วย ", "
        public string DescriptionRemark { get; set; } = string.Empty;

        public List<VisitItemDetailDto> Items { get; set; } = new();
    }

    public class VisitDailyReportDto
    {
        public VisitKpiDto Kpi { get; set; } = new();
        public List<VisitTrendPointDto> Trend { get; set; } = new();
        public List<VisitGroupRowDto> BySalesperson { get; set; } = new();
        public List<VisitGroupRowDto> ByCustomer { get; set; } = new();
        public List<VisitRowDto> Visits { get; set; } = new();

        // ── ตัวเลือก dropdown (scope ตาม BU) ────────────────────────────────
        public List<string> AvailableSalesEmployees { get; set; } = new();
        public List<string> AvailableCustomers { get; set; } = new();
    }
}
