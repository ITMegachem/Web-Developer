using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class OutboundDeliveryReportRequestDto
    {
        public DateTime? DeliveryDateFrom { get; set; }
        public DateTime? DeliveryDateTo { get; set; }
        public string? CustomerName { get; set; }
        public string? Material { get; set; }
        public string? RouteName { get; set; }
        public string? Plant { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class OutboundDeliveryReportItemDto
    {
        public DateTime? DeliveryDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? CustomerNameThai { get; set; }
        public string? MaterialDescription { get; set; }
        public string? BatchNo { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? ShipToName { get; set; }
        public string? ShipToAddress { get; set; }
        public decimal? NetWeight { get; set; }
        public decimal? GrossWeight { get; set; }
        public decimal? TotalGrossWeight { get; set; }
        public string? RouteNameThai { get; set; }
        public int? Month { get; set; }
        public int? Year { get; set; }
        public string? DeliveryDocument { get; set; }
        public string? Material { get; set; }
        public string? ReferenceSODocument { get; set; }
    }
}
