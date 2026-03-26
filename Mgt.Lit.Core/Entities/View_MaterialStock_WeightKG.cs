using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    // กำหนดชื่อ View ให้ตรงกับใน SQL Server //MB52
    [Table("View_MaterialStock_WeightKG")]
    public class View_MaterialStock_WeightKG
    {
        public string? Material { get; set; }
        public string? MaterialDescription { get; set; }
     
        public string? ProductGroup { get; set; }
        public string? Plant { get; set; }
        public string? StorageLocation { get; set; }
        public string? Batch { get; set; }
        public string? Unit { get; set; }
        public decimal? Qty_BaseUnit { get; set; } // สะกดให้ตรงกับ AS ใน SQL
        public decimal? NetWeight { get; set; }    // สะกดให้ตรงกับ AS ใน SQL
        public decimal? TotalWeightKG { get; set; }
       
    }
}
