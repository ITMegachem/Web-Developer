namespace Mgt.Lit.Core.DTOs.DashBoard
{
    public class CustomerOverviewFilter
    {
        public int? Year { get; set; }
        public string? SalesGroup { get; set; }          // BU — server บังคับตาม permission
        public string? SalesEmployeeBP { get; set; }      // server บังคับ (ล็อกเป็นของตัวเองถ้า UserRole เป็นพนักงานขายทั่วไป)
        public string? CustomerFullName { get; set; }     // Company / ลูกค้า
        public string? IndustryName { get; set; }         // [Industry Name]
    }

    // 1 แถว = (Customer × เดือน) — ฝั่งหน้า pivot เป็น matrix (3 metric ต่อเดือน)
    public class CustomerByMonthRowDto
    {
        public string Customer { get; set; } = string.Empty;
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    public class CustomerOverviewDto
    {
        public List<CustomerByMonthRowDto> ByMonth { get; set; } = new();

        // ตัวเลือก dropdown (scope ตาม BU + Year)
        public List<string> Industries { get; set; } = new();   // [Industry Name]
        public List<string> Customers { get; set; } = new();     // cascade ตาม industry ที่เลือก

        // ---- ค่ารวม (backend คำนวณ) ----
        public Dictionary<string, SalePerfTotalDto> RowTotals { get; set; } = new();  // key = Customer
        public Dictionary<int, SalePerfTotalDto> ColTotals { get; set; } = new();      // key = MonthId
        public SalePerfTotalDto GrandTotal { get; set; } = new();

        // ---- BU scope (server เป็นคนกำหนด) ----
        public List<string> AvailableBus { get; set; } = new();  // >1 = โชว์ dropdown
        public string? EffectiveBu { get; set; }                  // BU ที่ server ใช้จริง
    }
}
