using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class CustomerRequestDto
    {
        public string? CustomerId { get; set; }
        public string? Keyword { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
