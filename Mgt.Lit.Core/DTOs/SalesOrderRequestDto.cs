using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class SalesOrderRequestDto
    {
        public string? SalesOrganization { get; set; }
        public string? BillingDocument { get; set; }
        public DateTime? DocDateFrom { get; set; }
        public DateTime? DocDateTo { get; set; }
        public string? SoldToParty { get; set; }
        public string? Material { get; set; }


        // paging
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        

    }
    public class SalesOrderResponseDto
    {
        public string BillingDocument { get; set; }
        public DateTime BillingDocumentDate { get; set; }
        public string SalesOrderDocument { get; set; }

        public string SoldToParty { get; set; }
        public string SoldToName { get; set; }

        public string ShiptoCode { get; set; }
        public string ShipToName { get; set; }

        public string Material { get; set; }
        public string MaterialName { get; set; }

        public string SalesEmployee { get; set; }
    }
}
