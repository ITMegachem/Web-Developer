using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    public class View_UserPermission
    {
        public int UserID { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public int? CompanyID { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? UserRole { get; set; }
        public string? RoleKey { get; set; }
        public string? Division { get; set; }
        public string? Tier { get; set; }

        public bool Page1Access { get; set; }
        public bool Page2Access { get; set; }
        public bool Page3Access { get; set; }
        public bool Page4Access { get; set; }
        public string? DataScope { get; set; }
        public bool CanViewVendor { get; set; }
        public bool CanViewCost { get; set; }
        public bool CanViewCustomer { get; set; }
        public string? SalesOrganizationCode { get; set; }
    }
    public static class DataScopes
    {
        public const string Own = "OWN";
        public const string Division = "DIVISION";
        public const string Company = "COMPANY";
        public const string CrossCompany = "CROSS_COMPANY";

        public static string Normalize(string? raw)
        {
            var value = (raw ?? "").Trim().ToUpperInvariant();

            return value switch
            {
                "OWN" => Own,
                "DIVISION" => Division,
                "COMPANY" => Company,
                "CROSS_COMPANY" => CrossCompany,
                "ALL_COMPANY" => CrossCompany,
                "ALL_COMPANIES" => CrossCompany,
                "ALL" => CrossCompany,
                _ => Own
            };
        }
    }
}
