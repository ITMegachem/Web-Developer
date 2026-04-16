using System.Text.Json.Serialization;

namespace Mgt.Lit.WebFront.Models.SalesOrder
{
    public sealed class SalesOrderDto
    {
        public string BillingDocument { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string SalesOrderDocument { get; set; } = string.Empty;
        public string SoldTo { get; set; } = string.Empty;
        public string SoldToDescription { get; set; } = string.Empty;
        public string ShipTo { get; set; } = string.Empty;
        public string ShipToDescription { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialDescription { get; set; } = string.Empty;
        public string SalesEmployee { get; set; } = string.Empty;
        public string SoldToMappingAddress { get; set; } = string.Empty;
        public string ShipToMappingAddress { get; set; } = string.Empty;
    }

    public sealed class SalesOrderFilter
    {
        public string? SalesOrganization { get; set; }
        public string? Document { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? Material { get; set; }
        public DateTime? DocDateFrom { get; set; }
        public DateTime? DocDateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public sealed class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalItems { get; set; }
        public List<string> AvailableMonths { get; set; } = new();
    }

    public sealed class StockRequirementResponse
    {
        [JsonPropertyName("Material_Code")]
        public string MaterialCode { get; set; } = string.Empty;

        [JsonPropertyName("Plant")]
        public string Plant { get; set; } = string.Empty;

        [JsonPropertyName("_Detail")]
        public List<StockDetail> Detail { get; set; } = new();
    }

    public sealed class StockDetail
    {
        [JsonPropertyName("document_Date")]
        public DateTime DocumentDate { get; set; }

        [JsonPropertyName("element_Type")]
        public string ElementType { get; set; } = string.Empty;

        [JsonPropertyName("document_No")]
        public string DocumentNo { get; set; } = string.Empty;

        [JsonPropertyName("ref_Doc")]
        public string RefDoc { get; set; } = string.Empty;

        [JsonPropertyName("receipt_Reqmt_Qty")]
        public decimal ReceiptReqmtQty { get; set; }

        [JsonPropertyName("available_qty")]
        public decimal AvailableQty { get; set; }

        [JsonPropertyName("additional_Info_In")]
        public string AdditionalInfoIn { get; set; } = string.Empty;

        [JsonPropertyName("additional_Info_Out")]
        public string AdditionalInfoOut { get; set; } = string.Empty;

        [JsonPropertyName("vendor_Code")]
        public string VendorCode { get; set; } = string.Empty;

        [JsonPropertyName("vendor_Name")]
        public string VendorName { get; set; } = string.Empty;

        [JsonPropertyName("customer_Code")]
        public string CustomerCode { get; set; } = string.Empty;

        [JsonPropertyName("customer_Name")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("materialGroup")]
        public string? MaterialGroup { get; set; }

        [JsonPropertyName("materialGroupDescription")]
        public string? MaterialGroupDescription { get; set; }

        [JsonPropertyName("salesGroup")]
        public string? SalesGroup { get; set; }
    }

    public sealed class PlantDto
    {
        public string PlantCode { get; set; } = string.Empty;
        public string PlantName { get; set; } = string.Empty;
    }

    public sealed class WarehouseStockRequest
    {
        [JsonPropertyName("Material_Code")]
        public string? MaterialCode { get; set; }

        [JsonPropertyName("Batch_Code")]
        public string? BatchCode { get; set; }

        public List<object> Detail { get; set; } = new();
    }

    public sealed class ODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();
    }

    public sealed class WarehouseStockSapResponse
    {
        [JsonPropertyName("Material_Code")]
        public string MaterialCode { get; set; } = string.Empty;

        [JsonPropertyName("Batch_Code")]
        public string BatchCode { get; set; } = string.Empty;

        [JsonPropertyName("_Detail")]
        public List<WarehouseStockSapDetail> Detail { get; set; } = new();
    }

    public sealed class WarehouseStockSapDetail
    {
        [JsonPropertyName("Plant")]
        public string? Plant { get; set; }

        [JsonPropertyName("Material_Group")]
        public string? MaterialGroup { get; set; }

        [JsonPropertyName("Material_Code")]
        public string MaterialCode { get; set; } = string.Empty;

        [JsonPropertyName("Material_Name")]
        public string MaterialName { get; set; } = string.Empty;

        [JsonPropertyName("Batch_Code")]
        public string BatchCode { get; set; } = string.Empty;

        [JsonPropertyName("Unit")]
        public string Unit { get; set; } = string.Empty;

        [JsonPropertyName("Unrestricted_Stock")]
        public decimal UnrestrictedStock { get; set; }

        [JsonPropertyName("Blocked_Stock")]
        public decimal BlockedStock { get; set; }

        [JsonPropertyName("Stock_in_QI")]
        public decimal StockInQi { get; set; }

        [JsonPropertyName("Value_of_Unrestricted_Stock")]
        public decimal TotalValue { get; set; }

        [JsonPropertyName("Value_of_Blocked_Stock")]
        public decimal ValueOfBlockedStock { get; set; }

        [JsonPropertyName("Value_of_Stock_in_QI")]
        public decimal ValueOfStockInQi { get; set; }

        [JsonPropertyName("Price_KG")]
        public decimal PricePerKg { get; set; }

        [JsonPropertyName("EXP_Date")]
        public DateTime? ExpDate { get; set; }
    }

    public sealed class MaterialStockApiResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<MaterialStockItemDto> Items { get; set; } = new();
    }

    public sealed class MaterialStockItemDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialGroup { get; set; } = string.Empty;
        public string MaterialDescription { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;

        public decimal Unrestricted_Stock { get; set; }

        // เพิ่มตัวนี้เพื่อให้ตรงกับหน้า warehouse-stock-material
        public decimal? Blocked_Stock { get; set; }

        public string Unit { get; set; } = string.Empty;
        public decimal? TotalValue { get; set; }
        public string ConversionText { get; set; } = string.Empty;
        public DateTime? ExpDate { get; set; }
        public decimal? PricePerKg { get; set; }
        public decimal? costPerKg { get; set; }
        public decimal? UnitKg { get; set; }
        public decimal? Stock_in_QI { get; set; }
    }

    public sealed class MaterialLookup
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class SoldToLookupDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class MaterialConsumptionDto
    {
        public int Year { get; set; }

        // Quantity (เดิม)
        public decimal January { get; set; }
        public decimal February { get; set; }
        public decimal March { get; set; }
        public decimal April { get; set; }
        public decimal May { get; set; }
        public decimal June { get; set; }
        public decimal July { get; set; }
        public decimal August { get; set; }
        public decimal September { get; set; }
        public decimal October { get; set; }
        public decimal November { get; set; }
        public decimal December { get; set; }
        public decimal Total { get; set; }

        // ✅ KG (เพิ่มใหม่)
        public decimal? JanuaryKG { get; set; }
        public decimal? FebruaryKG { get; set; }
        public decimal? MarchKG { get; set; }
        public decimal? AprilKG { get; set; }
        public decimal? MayKG { get; set; }
        public decimal? JuneKG { get; set; }
        public decimal? JulyKG { get; set; }
        public decimal? AugustKG { get; set; }
        public decimal? SeptemberKG { get; set; }
        public decimal? OctoberKG { get; set; }
        public decimal? NovemberKG { get; set; }
        public decimal? DecemberKG { get; set; }
        public decimal? TotalKG { get; set; }

        public decimal? NetWeight { get; set; }
    }
    // NofReportRowDto.cs
    public class NofReportRowDto
    {
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? SoldToParty { get; set; }
        public string? SoldToName { get; set; }

        // key = "2026/01" ... "2026/12"
        public Dictionary<string, decimal> MonthlyQty { get; set; } = new();
    }

    // NofReportFilter.cs
    public class NofReportFilter
    {
        public string? Material { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

}