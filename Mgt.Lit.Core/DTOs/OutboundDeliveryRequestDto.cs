using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class OutboundDeliveryRequestDto
    {
        public string? DeliveryDocument { get; set; }
        public string? Material { get; set; }
        public string? Plant { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
