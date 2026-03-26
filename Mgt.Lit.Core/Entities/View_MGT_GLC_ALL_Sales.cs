using System;
using System.ComponentModel.DataAnnotations.Schema;

[Table("View_MGT_GLC_ALL_Sales", Schema = "dbo")]
public class View_MGT_GLC_ALL_Sales
{
    public string? System { get; set; } // เปลี่ยนเป็น string? เพื่อรองรับค่า NULL
    public string? SalesOrganization { get; set; }
    public string? BillingDocument { get; set; }
    public string? BillingDocumentType { get; set; }
    public DateTime? BillingDocumentDate { get; set; } // ใช้ DateTime? เผื่อวันที่ว่าง
    public DateTime? DeliveryDate { get; set; }

    public string? SoldToParty { get; set; }
    public string? ShiptoCode { get; set; }
    public string? CustomerFullName { get; set; }
    public string? TransactionCurrency { get; set; }
    public string? OverallBillingStatus { get; set; }
    public string? SalesDocument { get; set; }
    public string? ReferenceSDDocument { get; set; }
    public string? SalesGroup { get; set; }
    public string? SalesOffice { get; set; }
    public string? Material { get; set; }
    public string? MaterialName { get; set; }
    public string? ProductGroup { get; set; }

    [Column("Industry Code")]
    public string? IndustryCode { get; set; }
    [Column("Industry Name")]
    public string? IndustryName { get; set; }
    [Column("Affiliate Customer Code")]
    public string? AffiliateCustomerCode { get; set; }
    [Column("Affiliate Customer Name")]
    public string? AffiliateCustomerName { get; set; }

    // แก้ปัญหา "Customer Area/Type ไม่เข้า" โดยเพิ่มคอลัมน์เหล่านี้
    [Column("Customer Area Code")]
    public string? CustomerAreaCode { get; set; }
    [Column("Customer Area Name")]
    public string? CustomerAreaName { get; set; }
    [Column("Customer Type Code")]
    public string? CustomerTypeCode { get; set; }
    [Column("Customer Type Name")]
    public string? CustomerTypeName { get; set; }

    [Column("MaterialGroup1")]
    public string? MaterialGroup1 { get; set; }
    [Column("Material Group Name")]
    public string? MaterialGroupName { get; set; }
    [Column("MaterialGroup2")]
    public string? MaterialGroup2 { get; set; }

    [Column("Division Name")] // แก้ไขเรื่อง Division Name ไม่เข้า
    public string? DivisionName { get; set; }

    [Column("SalesEmployeeID")]
    public string? SalesEmployeeID { get; set; }

    [Column("Soldto-name")]
    public string? SoldToName { get; set; }
    [Column("Soldto-adress")]
    public string? SoldToAddress { get; set; }
    [Column("Shipto-name")]
    public string? ShipToName { get; set; }
    [Column("Shipto-adress")]
    public string? ShipToAddress { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? GrossProfit { get; set; }

    public int? x_Days { get; set; }
    public int? x_Month { get; set; }
    public int? x_Year { get; set; }

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
}