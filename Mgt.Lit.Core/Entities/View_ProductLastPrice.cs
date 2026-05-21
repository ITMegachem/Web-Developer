using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class View_ProductLastPrice
    {
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public DateTime? LastSaleDate { get; set; }
        public double? LastPricePerPack { get; set; }
        public double? LastPrice_PerKG { get; set; }
        public double? CostPerPack { get; set; }
        public double? CostPerKG { get; set; }
    }
}
