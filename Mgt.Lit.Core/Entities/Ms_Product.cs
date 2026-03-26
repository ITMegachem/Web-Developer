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
    }
}
