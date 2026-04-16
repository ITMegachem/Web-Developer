namespace Mgt.Lit.WebFront.Services.Models
{
    public class ApiPagedResponse
    {
        public int TotalCount { get; set; }
        public List<ApiSalesOrderItem> Items { get; set; } = new();
    }
    public class ApiSalesOrderItem
    {
        public string? BillingDocument { get; set; }
        public DateTime? BillingDocumentDate { get; set; }  // ← DateTime?
        public DateTime? DeliveryDate { get; set; }
        public DateTime? DocDate { get; set; }
        public string? SalesOrderDocument { get; set; }
        public string? SoldToParty { get; set; }
        public string? SoldToName { get; set; }
        public string? SoldToAddress { get; set; }          // ← เปลี่ยนจาก SoldToMappingAddress
        public string? ShiptoCode { get; set; }
        public string? ShipToName { get; set; }
        public string? ShipToAddress { get; set; }          // ← เปลี่ยนจาก ShipToMappingAddress
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? SalesEmployee { get; set; }
    }
}
