using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mgt.Lit.Core.Entities
{
    // ซิงก์จาก Zoho CRM v8 /crm/v8/Deals — 1 แถว = 1 Opportunity/Deal (ไม่ซ้ำ Deal ID)
    [Table("MGT_Deal")]
    public class MGT_Deal
    {
        [Key]
        public string DealId { get; set; } = string.Empty;
        public string? OpportunityName { get; set; }
        public DateTime? CreatedDate { get; set; }

        // Closing_Date จาก Zoho (วันที่คาดว่าจะปิด) — ใช้ fallback เมื่อไม่มี ActualClosedDate
        public DateTime? ClosingDate { get; set; }

        // วันที่ปิดจริง (Stage เปลี่ยนเป็น Closed Won/Lost) — ควรใช้ตัวนี้เป็นหลักสำหรับ Win Rate/Sales Cycle
        public DateTime? ActualClosedDate { get; set; }

        // Deal Stage / Status — ค่าจริงขึ้นกับที่แต่ละองค์กรตั้งชื่อเอง (เช่น "Closed Won - Exact Match", "Close Lost - Expiry Date")
        public string? Stage { get; set; }

        // การจัดกลุ่ม Stage ของ Zoho เอง: "Pipeline" (ยังเปิดอยู่) / "Closed" (Won) / "Omitted" (Lost/Cancelled)
        // ดึงมาจาก forecast_category ของแต่ละ Stage picklist value ตอน sync — ใช้ตัวนี้แทนการเทียบชื่อ Stage ตรงๆ
        // เพราะชื่อ Stage ของแต่ละองค์กรไม่ตรงกับ "Closed Won"/"Closed Lost" มาตรฐานเสมอไป
        public string? ForecastCategory { get; set; }

        public string? SalesGroup { get; set; }         // BU — sync จาก Account_Name.Sales_Group_BU (custom field บน Account ไม่ใช่ Deal)
        public string? SalesEmployeeBP { get; set; }    // Deal Owner
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? IndustryName { get; set; }
        public decimal? DealAmount { get; set; }
        public string? LostReason { get; set; }         // มีค่าเฉพาะตอน Stage = Closed Lost

        // Zoho field "Forecast_Status" (picklist: -None-/Yes/No) — Sales ทำเครื่องหมายเองว่า deal นี้
        // "นับเข้า forecast รอบนี้" หรือไม่ ใช้เป็นตัวกรองหลักของ Sales Forecast Accuracy Report
        public string? ForecastStatus { get; set; }
    }

    // 1 Deal อาจมีหลาย Product (Zoho subform) — แยกตารางเพื่อไม่ให้นับ Deal ซ้ำตอนวิเคราะห์ระดับ Deal
    // ใช้เฉพาะตอนวิเคราะห์ "Win Rate by Product" เท่านั้น (join กลับไปที่ MGT_Deal เพื่อดู Stage/Amount)
    [Table("MGT_DealProduct")]
    public class MGT_DealProduct
    {
        [Key]
        public int Id { get; set; }
        public string DealId { get; set; } = string.Empty;
        public string? Product { get; set; }   // ชื่อสินค้า (Material_Name) — ใช้ fallback เป็น Material_Code ถ้าไม่มีชื่อ

        // Zoho field "Material_Group" บน Deal_Items subform (text) — ใช้จัดกลุ่มสินค้าสำหรับ Sales Productivity Report
        public string? MaterialGroup { get; set; }

        // ── เพิ่มสำหรับ Forecast Monthly Report — ต้อง join กับ SAP Material Stock ด้วยรหัสจริง ──
        public string? MaterialCode { get; set; }     // Material_Code เสมอ (ต่างจาก Product ที่อาจเป็นชื่อ)
        public decimal? Quantity { get; set; }         // จำนวนที่ Forecast ไว้ในบรรทัดนี้
        public DateTime? RequiredDate { get; set; }    // เดือนที่ต้องการสินค้า — ใช้ pivot เป็นคอลัมน์รายเดือน
        public string? Unit { get; set; }
    }

    // ── Sales Forecast Accuracy Report ──────────────────────────────────────────
    // "ภาพถ่าย" ของ Deal ที่ยังเปิดอยู่ (ForecastCategory = Pipeline) ณ วันที่ capture — เก็บไว้ทุกครั้งที่ sync
    // เพื่อให้มี Forecast ที่ "ล็อกไว้ก่อนรู้ Actual" ย้อนหลังได้ในอนาคต (วันนี้ยังไม่มีข้อมูลเก่า เริ่มสะสมจากนี้ไป)
    // 1 แถว = 1 Deal ณ 1 SnapshotDate ที่ยังเปิดอยู่ และมี ClosingDate (เดือนที่กำลังพยากรณ์)
    [Table("MGT_ForecastSnapshot")]
    public class MGT_ForecastSnapshot
    {
        [Key]
        public int Id { get; set; }

        public DateTime SnapshotDate { get; set; }      // วันที่ capture (ปกติ = วันที่ sync ล่าสุด)
        public int ForecastYear { get; set; }            // ปีของเดือนที่กำลังพยากรณ์ (จาก Deal.ClosingDate)
        public int ForecastMonth { get; set; }           // เดือนที่กำลังพยากรณ์ (จาก Deal.ClosingDate)

        public string DealId { get; set; } = string.Empty;
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? CustomerName { get; set; }
        public string? IndustryName { get; set; }
        public decimal ForecastAmount { get; set; }
    }
}
