using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class OutboundDeliveryReportRequestDto
    {
        public DateTime? DeliveryDateFrom { get; set; }
        public DateTime? DeliveryDateTo { get; set; }

        // ✅ เพิ่ม
        public DateTime? DocumentDateFrom { get; set; }
        public DateTime? DocumentDateTo { get; set; }

        public string? CustomerName { get; set; }
        public string? Material { get; set; }
        public string? RouteName { get; set; }
        public string? Plant { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class OutboundDeliveryReportItemDto
    {
        public DateTime? DeliveryDate { get; set; }       // 1. Date
        public string? CustomerNameEn { get; set; }
        public string? SoldToParty { get; set; }           // 2. Sold-to
        public string? CustomerNameThai { get; set; }      // 3. Customer Name (Thai)
        public string? MaterialDescription { get; set; }   // 4. Material Description
        public string? BatchNo { get; set; }               // 5. Batch No.
        public decimal? Quantity { get; set; }             // 6. Quant.
        public string? Unit { get; set; }                  // 7. Pack (unit)
        public string? ShipToName { get; set; }            // 8. Ship To Name
        public string? ShipToAddress { get; set; }         // 8. Ship To Address
        public string? License { get; set; }               // 9. LICENSE (YY1_MM_TRANSPORTCLASS_PRD)
        public string? ClassNo { get; set; }               // 10. Class No.
        public decimal? NetWeight { get; set; }            // 11. Net Weight
        public decimal? GrossWeight { get; set; }          // 12. Gross Weight
        public decimal? TotalGrossWeight { get; set; }     // 13. Total Gross Weight (Qty * GrossWeight)
        public string? RouteNameThai { get; set; }         // 14. Route Name (Thai customer name)
        public int? Month { get; set; }                    // 15. Month
        public int? Year { get; set; }                     // 16. Year
                                                           // extras for reference
        public string? DeliveryDocument { get; set; }
        //public string? CustomerNameEn { get; set; }
        public string? Material { get; set; }
        public string? ReferenceSODocument { get; set; }
        public string? StorageClassCode { get; set; }
        public string? StorageClassDescription { get; set; }
    }
}
