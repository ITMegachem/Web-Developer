using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class SalesOrderRequestDto
    {
        public string? SalesOrganization { get; set; }
        public string? Document { get; set; }           // ← ต้องมีชื่อนี้
        public string? BillingDocument { get; set; }    // ← ถ้ามีอันนี้ด้วย อาจสับสน
        public DateTime? DeliveryDateFrom { get; set; }
        public DateTime? DeliveryDateTo { get; set; }
        public string? SoldToParty { get; set; }
        public string? Material { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
    public class SalesOrderResponseDto
    {
        public string BillingDocument { get; set; }
        public DateTime BillingDocumentDate { get; set; }
        public string SalesOrderDocument { get; set; }

        public string SoldToParty { get; set; }
        public string SoldToName { get; set; }
        public string SoldToMappingAddress { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string ShiptoCode { get; set; }
        public string ShipToName { get; set; }
        public string ShipToMappingAddress { get; set; }

        public string Material { get; set; }
        public string MaterialName { get; set; }

        public string SalesEmployeeID { get; set; }
    }
    // Request
    public class NofReportRequestDto
    {
        public string? Material { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? ProductGroup { get; set; }
    }

    // Response
    public class NofReportResponseDto
    {
        public string? BillingDocument { get; set; }
        public DateTime? BillingDocumentDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? SoldToName { get; set; }
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? ProductGroup { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? NetAmount { get; set; }
        public decimal? CostAmount { get; set; }
        public decimal? GrossProfit { get; set; }
        public string? SalesEmployeeID { get; set; }
        public decimal? QuantityKG { get; set; }
        public decimal? NetWeight { get; set; }
    }
    // ── Request ──────────────────────────────────────────────────────────
    public class ComprehensiveReportRequestDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public DateTime? BillingDateFrom { get; set; }
        public DateTime? BillingDateTo { get; set; }
        public string? SoldToParty { get; set; }
        public string? Material { get; set; }
        public string? SalesGroup { get; set; }
        public string? ProductGroup { get; set; }
        public DateTime? DeliveryDateFrom { get; set; }  // ✅ เพิ่ม
        public DateTime? DeliveryDateTo { get; set; }  // ✅ เพิ่ม
        public string? CustomerGroup { get; set; }
    }

    // ── Response ─────────────────────────────────────────────────────────
    public class ComprehensiveReportResponseDto
    {
        // จาก MGT_Sale
        public string? BillingDocument { get; set; }
        public DateTime? BillingDocumentDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? CustomerFullName { get; set; }
        public string? TransactionCurrency { get; set; }
        public string? ReferenceSDDocument { get; set; }
        public string? SalesGroup { get; set; }
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? MaterialGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? SoldToName { get; set; }
        public string? SoldToAddress { get; set; }
        public double? NetAmount { get; set; }
        public double? CostAmount { get; set; }
        public double? GrossProfit { get; set; }
        public double? Quantity { get; set; }
        public string? Unit { get; set; }
        // จาก View_ProductLastPrice
        public DateTime? LastSaleDate { get; set; }
        public double? LastPricePerPack { get; set; }
        public double? LastPrice_PerKG { get; set; }
        public double? CostPerPack { get; set; }
        public double? CostPerKG { get; set; }
        public string? CustomerGroup { get; set; }
        public string? MaterialGroup1Description { get; set; }  // จาก Ms_MaterialGroup1
        public double? GrossMarginPct { get; set; }  // GP% = GrossProfit/NetAmount*100
    }
}
