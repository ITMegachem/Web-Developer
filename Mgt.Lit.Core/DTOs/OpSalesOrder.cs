using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class OpSalesOrderRequestDto
    {
        public string? SalesOrder { get; set; }
        public string? SoldToParty { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
    // Entity
    public class View_Sales_with_Op_SalesOrder
    {
        public string? SalesOrder { get; set; }
        public string? System_Name { get; set; }
        public DateTime? SalesOrderDate { get; set; }
        public string? SalesOrderType { get; set; }
        public string? SalesOrganization { get; set; }
        public string? DistributionChannel { get; set; }
        public string? SalesGroup { get; set; }
        public string? SoldToParty { get; set; }
        public string? PurchaseOrderByCustomer { get; set; }
        public decimal? TotolNetAmont { get; set; }
        public string? OverallDeliveryStatus { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
        public string? CustomerPaymentTerms { get; set; }
        public DateTime? BillingDoucumentDate { get; set; }
        public string? PartnerFunction { get; set; }
        public string? Customer { get; set; }
        public string? AddressID { get; set; }
    }
    public class SalesOrderDetailRequestDto
    {
        public string SalesOrder { get; set; } = "";
    }
    public class Op_SalesOrder
    {
        public string SalesOrder { get; set; } = "";
        public string? System_Name { get; set; }
        public DateTime? SalesOrderDate { get; set; }
        public string? SalesOrderType { get; set; }
        public string? SalesOrderTypeInternalCode { get; set; }
        public string? SalesOrganization { get; set; }          // ← มีใน Table
        public string? DistributionChannel { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesOffice { get; set; }
        public string? SalesDistrict { get; set; }
        public string? SoldToParty { get; set; }
        public string? PurchaseOrderByCustomer { get; set; }
        public decimal? TotolNetAmont { get; set; }
        public string? OverallDeliveryStatus { get; set; }
        public string? TrabsactionCurrency { get; set; }
        public DateTime? PricingDate { get; set; }
        public string? PriceDetnExchangeRate { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }    // ← มีใน Table
        public string? CustomerPaymentTerms { get; set; }
        public string? OverallISDProcessStatus { get; set; }
        public string? OverallTotalDeliveryStatus { get; set; }
        public DateTime? BillingDoucumentDate { get; set; }
    }
}
