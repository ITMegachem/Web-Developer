using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class ProductUnitOfMeasureDto
    {
        public int Id { get; set; }
        public string? Product { get; set; }
        public string? ProductDescription { get; set; }
        public string? AlternativeUnit { get; set; }
        public double? QuantityDenominator { get; set; }
        public double? QuantityNumerator { get; set; }
        public string? BaseUnit { get; set; }
        public double? GrossWeight { get; set; }
        public string? WeightUnit { get; set; }
        public DateTime? UpdateDate { get; set; }
    }
}
