using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs.Admin
{
    public class DashboardRequestDto
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }
    public class ActivityFilterDto
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Username { get; set; }
        public bool? IsSuccess { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Status { get; set; }
    }
    public class ErrorFilterDto
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public DateTime CreateDate { get; set; }
    }
    public class PagedResult<T>
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<T> Items { get; set; }
    }
}
