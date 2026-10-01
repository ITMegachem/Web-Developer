using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class MaterialConsumptionRequest
    {
        public string Material { get; set; }
        public int? YearFrom { get; set; }   // ✅ เพิ่ม — ไม่ส่งมา = 2020
        public int? YearTo { get; set; }     // ✅ เพิ่ม — ไม่ส่งมา = ปีปัจจุบัน
    }
}
