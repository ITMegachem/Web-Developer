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

        // ✅ รับเป็น string ก่อน แล้วค่อย parse ใน C#
        public string NetWeight { get; set; }

        // ✅ Property สำหรับใช้งานจริง (ไม่ map กับ DB)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal? NetWeightDecimal =>
            decimal.TryParse(NetWeight, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
