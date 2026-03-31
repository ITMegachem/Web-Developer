using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Mgt.Lit.Core.DTOs
{
    public class MaterialStockRequestDto
    {
        public string? Plant { get; set; }
        public string? MaterialGroup { get; set; }
        public string? Material { get; set; }
        public string? Batch { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 100;

        [JsonPropertyName("Material_Code")]
        public string? Material_Code { get; set; }

        [JsonPropertyName("Batch_Code")]
        public string? Batch_Code { get; set; }
        
    }

    public class MaterialStockResponseDto
    {
        public string MaterialCode { get; set; }

        public string MaterialDescription { get; set; }

        public string BatchNo { get; set; }

        public string Plant { get; set; }

        public string StorageLocation { get; set; }

        public decimal Unrestricted_Stock { get; set; }

        public string Unit { get; set; }

        public DateTime? ExpDate { get; set; }

        public decimal QualityInspection { get; set; }

        public decimal CostPerKg { get; set; }

        public decimal TotalValue { get; set; }

        public string MaterialGroup { get; set; }

        public string ConversionText { get; set; }
        public decimal UnitKg { get; set; }
        //public string? SalesGroup { get; set; }
    }

    public class StockMaterialRequestDto
    {
        [JsonPropertyName("Material_Code")]
        public string? Material_Code { get; set; }

        [JsonPropertyName("Batch_Code")]
        public string? Batch_Code { get; set; }
    }
}