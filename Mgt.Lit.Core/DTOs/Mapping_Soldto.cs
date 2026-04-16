using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class Mapping_Soldto
    {
        public string SalesOrder { get; set; } = "";
        public string? PartnerFunction { get; set; }
        public string? PartnerFunctionInternalCode { get; set; }
        public string? Customer { get; set; }
        public string? AddressID { get; set; }
        public string? FullName { get; set; }
        public string? Address { get; set; }
    }
}
