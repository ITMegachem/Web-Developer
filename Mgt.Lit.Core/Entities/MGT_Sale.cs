using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    [Table("MGT_Sale")]
    public class MGT_Sale
    {
        public string? BillingDocument { get; set; }
        public DateTime? BillingDocumentDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? CustomerFullName { get; set; }
        public string? TransactionCurrency { get; set; }
        public string? ReferenceSDDocument { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesOrganization { get; set; }
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? ProductGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        [Column("Soldto-name")]
        public string? SoldtoName { get; set; }
        [Column("Soldto-adress")]
        public string? SoldtoAdress { get; set; }
        public decimal? NetAmount { get; set; }
        public decimal? CostAmount { get; set; }
        public decimal? GrossProfit { get; set; }
        public double? Quantity { get; set; }
        public string? Unit { get; set; }

        // ✅ เพิ่มใหม่
        public string? MaterialGroup1 { get; set; }

        [Column("Material Group Name")]
        public string? MaterialGroupName { get; set; }

        public string? MaterialGroup2 { get; set; }

        [Column("Industry Code")]
        public string? IndustryCode { get; set; }

        [Column("Industry Name")]
        public string? IndustryName { get; set; }

        [Column("Affiliate Customer Code")]
        public string? AffiliateCustomerCode { get; set; }

        [Column("Affiliate Customer Name")]
        public string? AffiliateCustomerName { get; set; }

        [Column("Customer Area Code")]
        public string? CustomerAreaCode { get; set; }

        [Column("Customer Area Name")]
        public string? CustomerAreaName { get; set; }

        [Column("Customer Type Code")]
        public string? CustomerTypeCode { get; set; }

        [Column("Customer Type Name")]
        public string? CustomerTypeName { get; set; }

        public string? DivisionName { get; set; }
        
    }
}
