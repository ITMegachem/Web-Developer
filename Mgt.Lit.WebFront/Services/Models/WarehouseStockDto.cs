using System.Text.Json.Serialization;

namespace Mgt.Lit.WebFront.Services.Models
{
    public class WarehouseStockDto
    {
        [JsonPropertyName("materialCode")]
        public string MaterialCode { get; set; } = "";

        [JsonPropertyName("materialDescription")]
        public string MaterialDescription { get; set; } = "";

        [JsonPropertyName("batchNo")]
        public string BatchNo { get; set; } = "";

        [JsonPropertyName("plant")]
        public string Plant { get; set; } = "";

        [JsonPropertyName("storageLocation")]
        public string StorageLocation { get; set; } = "";

        [JsonPropertyName("unrestricted_Stock")]
        public decimal Unrestricted_Stock { get; set; }

        [JsonPropertyName("blocked_Stock")]
        public decimal Blocked_Stock { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = "";

        [JsonPropertyName("expDate")]
        public DateTime? ExpDate { get; set; }

        [JsonPropertyName("qualityInspection")]
        public decimal QualityInspection { get; set; }

        [JsonPropertyName("costPerKg")]
        public decimal CostPerKg { get; set; }

        [JsonPropertyName("totalValue")]
        public decimal TotalValue { get; set; }

        [JsonPropertyName("unitKg")]
        public decimal UnitKg { get; set; }

        [JsonPropertyName("materialGroup")]
        public string? MaterialGroup { get; set; }

        [JsonPropertyName("conversionText")]
        public string ConversionText { get; set; } = "";
        [JsonPropertyName("Stock_in_QI")]
        public decimal Stock_in_QI { get; set; }
    }

    public class WarehouseStockFilter
    {
        public string? Plant { get; set; }
        public string? MaterialGroup { get; set; }
        public string? Material { get; set; }
        public string? Batch { get; set; }
        public string? MaterialName { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}