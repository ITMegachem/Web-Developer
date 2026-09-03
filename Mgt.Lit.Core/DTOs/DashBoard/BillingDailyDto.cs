using System;

namespace Mgt.Lit.Core.DTOs.DashBoard
{
    public class BillingDailyFilter
    {
        public string? SalesGroup { get; set; }          // BU — server บังคับตาม permission
        public string? CustomerFullName { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public DateTime? DateFrom { get; set; }           // ช่วงวันที่: จาก
        public DateTime? DateTo { get; set; }             // ช่วงวันที่: ถึง
    }

    // 1 แถว = billing document × material (detail grid)
    public class BillingDailyRowDto
    {
        public string BillDoc { get; set; } = string.Empty;
        public DateTime? BillDate { get; set; }
        public string SaleEm { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string ProductGroup { get; set; } = string.Empty;
        public int MonthId { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class BillingDailyDto
    {
        public List<BillingDailyRowDto> Rows { get; set; } = new();

        // ตัวเลือก dropdown (scope ตาม BU + ช่วงวันที่)
        public List<string> Customers { get; set; } = new();
        public List<string> SalesEmployees { get; set; } = new();

        public decimal GrandTotal { get; set; }   // ยอดรวมทั้งหมดที่กรองได้
    }
}