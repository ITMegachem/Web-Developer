using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class Ms_MaterialGroup1
    {
        [Key] // 🔥 เพิ่มบรรทัดนี้
        public string MaterialGroup { get; set; }
        public string Description { get; set; }
    }
    public class MaterialGroupDto
    {
        public string Plant { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }
}
