// วางไว้ใน Mgt.Lit.WebFront/Models/SalesOrder/ (ไฟล์ใหม่)
using System.Text.Json.Serialization;

namespace Mgt.Lit.WebFront.Models.SalesOrder
{
    public class MaterialDocumentRequestModel
    {
        public string? Material { get; set; }
        public string? Plant { get; set; }
        public string? StorageLocation { get; set; }
        public string? Batch { get; set; }

        public DateTime? PostingDateFrom { get; set; }
        public DateTime? PostingDateTo { get; set; }

        public string? MovementType { get; set; }
        public string? MaterialDocument { get; set; }
        public string? MaterialDocumentYear { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 200;
    }

    public class MaterialDocumentItemModel
    {
        public string? MaterialDocument { get; set; }
        public string? MaterialDocumentYear { get; set; }
        public string? MaterialDocumentItem { get; set; }

        public DateTime? PostingDate { get; set; }
        public DateTime? DocumentDate { get; set; }

        public string? Material { get; set; }
        public string? MaterialDescription { get; set; }
        public string? Plant { get; set; }
        public string? StorageLocation { get; set; }
        public string? Batch { get; set; }

        public string? MovementType { get; set; }
        public string? MovementTypeDescription { get; set; }
        public string? DebitCreditCode { get; set; }

        public decimal? Quantity { get; set; }
        public string? EntryUnit { get; set; }
        public decimal? QuantityInBaseUnit { get; set; }
        public string? BaseUnit { get; set; }

        public decimal? Amount { get; set; }
        public string? Currency { get; set; }

        public string? RefDocType { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? DeliveryDocument { get; set; }
        public string? SalesOrder { get; set; }
        public string? Customer { get; set; }
        public string? Supplier { get; set; }
        public string? Reservation { get; set; }

        public string? ItemText { get; set; }
        public string? CreatedByUser { get; set; }
        public DateTime? ShelfLifeExpirationDate { get; set; }
        public bool IsCancelled { get; set; }
        public string? ReversedMaterialDocument { get; set; }
    }

    public class MaterialDocumentResponseModel
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<MaterialDocumentItemModel> Items { get; set; } = new();
    }
}