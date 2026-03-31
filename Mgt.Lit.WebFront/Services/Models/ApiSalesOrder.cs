namespace Mgt.Lit.WebFront.Services.Models
{
    public class ApiPagedResponse
    {
        public int TotalCount { get; set; }
        public List<ApiSalesOrderItem> Items { get; set; } = new();
    }
    public class ApiSalesOrderItem
    {
        public string BillingDocument { get; set; } = "";
        public DateTime BillingDocumentDate { get; set; }
        public string SalesOrderDocument { get; set; } = "";
        public string SoldToParty { get; set; } = "";
        public string SoldToName { get; set; } = "";
        public string ShiptoCode { get; set; } = "";
        public string ShipToName { get; set; } = "";
        public string Material { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public string SalesEmployee { get; set; } = "";
        public string? SoldToMappingAddress { get; set; }  // ✅ เพิ่ม
        public string? ShipToMappingAddress { get; set; }  // ✅ เพิ่ม

    }
}
