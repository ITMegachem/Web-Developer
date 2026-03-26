using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class DownloadLog
    {
        public int Id { get; set; }
        public string UserID { get; set; } = "";
        public string Username { get; set; } = "";
        public string? FullName { get; set; }
        public string? CompanyID { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? Division { get; set; }
        public string? UserRole { get; set; }
        public string? SalesOrganizationCode { get; set; }
        public string FileName { get; set; } = "";
        public DateTime DownloadedAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
    public class DownloadLogRequest
    {
        public string? Username { get; set; }
        public string? FileName { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}
