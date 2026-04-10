using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.WebApi.Controllers.SalesOrder
{
    internal class OpSalesOrder 
    {
        public string SalesOrder { get; set; }
        public string System_Name { get; set; }
        public string SalesOrderType { get; set; }
        public string SalesOrganization { get; set; }
        public string DistributionChannel { get; set; }
        public string SoldToParty { get; set; }
        public string PurchaseOrderByCustomer { get; set; }
        public string TrabsactionCurrency { get; set; }
        public DateTime SalesOrderDate { get; set; }
    }
}