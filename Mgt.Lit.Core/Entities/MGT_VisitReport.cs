using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mgt.Lit.Core.Entities
{
    // ซิงก์จาก Zoho CRM v8 module "Visit_Reports" — 1 แถว = 1 ใบเข้าเยี่ยมลูกค้า (VP-00001, VP-00002, ...)
    [Table("MGT_VisitReport")]
    public class MGT_VisitReport
    {
        [Key]
        public string VisitReportId { get; set; } = string.Empty;   // Zoho record id
        public string? VisitNumber { get; set; }        // Name (เช่น "VP-000033")
        public string? SalesEmployeeBP { get; set; }     // Owner.name
        public DateTime? VisitDate { get; set; }          // Date

        public string? CustomerCode { get; set; }         // Account_Name.id
        public string? CustomerName { get; set; }         // Account_Name.name
        // BU — sync จาก Account_Name.Sales_Group_BU (custom field บน Account เดียวกับที่ใช้ใน MGT_Deal)
        public string? SalesGroup { get; set; }

        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? DescriptionRemark { get; set; }

        public DateTime? CreatedTime { get; set; }
        public DateTime? ModifiedTime { get; set; }
    }

    // Visit_Items (Zoho subform) — 1 แถว = 1 สินค้า/หัวข้อที่คุยระหว่างการเข้าเยี่ยมครั้งนั้น
    // field ส่วนใหญ่เป็น free-text ตามที่ Sales กรอกจริงใน Zoho (ไม่ใช่ตัวเลข/boolean มาตรฐาน)
    [Table("MGT_VisitItem")]
    public class MGT_VisitItem
    {
        [Key]
        public int Id { get; set; }
        public string VisitReportId { get; set; } = string.Empty;   // FK -> MGT_VisitReport

        public string? MaterialName { get; set; }
        public string? MaterialCode { get; set; }
        public string? MaterialGroup { get; set; }

        public string? CompetitorName { get; set; }
        public string? CompetitorSupplier { get; set; }

        public string? ContactPerson { get; set; }
        public string? Position { get; set; }

        public string? Price { get; set; }          // free text เช่น "710 THB/Kg"
        public string? Consumption { get; set; }     // free text
        public string? MakerOrigin { get; set; }
        public string? Application { get; set; }
        public string? Potential { get; set; }        // free text เช่น "Yes" (ไม่ใช่ boolean)
        public string? StatusNote { get; set; }
    }
}
