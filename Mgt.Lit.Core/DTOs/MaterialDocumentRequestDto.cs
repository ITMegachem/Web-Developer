using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class MaterialDocumentRequestDto
    {
        public string? Material { get; set; }
        public string? Plant { get; set; }
        public string? StorageLocation { get; set; }
        public string? Batch { get; set; }

        public DateTime? PostingDateFrom { get; set; }
        public DateTime? PostingDateTo { get; set; }

        public string? MovementType { get; set; }      // เช่น "101", "601"
        public string? MaterialDocument { get; set; }  // ค้นเลขเอกสารตรง ๆ
        public string? MaterialDocumentYear { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class MaterialDocumentItemDto
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
        public string? DebitCreditCode { get; set; }   // S = รับเข้า, H = จ่ายออก

        public decimal? Quantity { get; set; }         // ใส่เครื่องหมายตาม D/C แล้ว
        public string? EntryUnit { get; set; }
        public decimal? QuantityInBaseUnit { get; set; }
        public string? BaseUnit { get; set; }

        public decimal? Amount { get; set; }           // ซ่อนถ้าไม่มีสิทธิ์ดูต้นทุน
        public string? Currency { get; set; }

        public string? RefDocType { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? DeliveryDocument { get; set; }
        public string? SalesOrder { get; set; }
        public string? Customer { get; set; }
        public string? Supplier { get; set; }          // ซ่อนถ้าไม่มีสิทธิ์ดู vendor
        public string? Reservation { get; set; }

        public string? ItemText { get; set; }
        public string? CreatedByUser { get; set; }
        public DateTime? ShelfLifeExpirationDate { get; set; }
        public bool IsCancelled { get; set; }
        public string? ReversedMaterialDocument { get; set; }
    }
}
