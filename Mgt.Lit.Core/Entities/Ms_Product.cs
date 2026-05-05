using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class Ms_Product
    {
        [Key]
        public string Product { get; set; }
        public string ProductGroup { get; set; }

        public string? NetWeight { get; set; } // ← nvarchar
        public double? GrossWeight { get; set; } // ← float
        public string? WeightUnit { get; set; } // ← nvarchar

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal? NetWeightDecimal =>
            decimal.TryParse(NetWeight, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal? GrossWeightDecimal =>
            GrossWeight.HasValue ? (decimal?)Convert.ToDecimal(GrossWeight) : null;
    }
}
