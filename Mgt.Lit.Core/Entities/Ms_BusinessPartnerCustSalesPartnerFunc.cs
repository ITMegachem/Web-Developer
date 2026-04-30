using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class Ms_BusinessPartnerCustSalesPartnerFunc
    {
        public int Id { get; set; }
        public string? Customer { get; set; }
        public string? SalesOrganization { get; set; }
        public string? DistributionChannel { get; set; }
        public string? Division { get; set; }
        public string? PartnerFunction { get; set; }
        public string? BPCustomerNumber { get; set; }
        public string? AuthorizationGroup { get; set; }
        public DateTime? CreateDate { get; set; }
        public DateTime? UpdateDate { get; set; }
    }
}
