namespace Mgt.Lit.Core.DTOs.DashBoard
{
    public class ProductOverviewFilter
    {
        public int? Year { get; set; }
        public string? SalesGroup { get; set; }        // BU — server บังคับตาม permission
        public string? SalesEmployeeBP { get; set; }   // server บังคับ (ล็อกเป็นของตัวเองถ้า UserRole เป็นพนักงานขายทั่วไป)
        public string? MaterialGroupName { get; set; }
        public string? MaterialName { get; set; }
    }

    // 1 แถว = (Material × เดือน) — ฝั่งหน้า pivot เป็น matrix
    public class ProductByMonthRowDto
    {
        public string MaterialName { get; set; } = string.Empty;
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    public class ProductOverviewDto
    {
        public List<ProductByMonthRowDto> ByMonth { get; set; } = new();

        // ตัวเลือกสำหรับ dropdown (cascade)
        public List<string> MaterialGroups { get; set; } = new();   // กรองตาม BU + Year
        public List<string> Materials { get; set; } = new();        // กรองตาม BU + Year + Group

        // ---- ค่ารวม (backend คำนวณ) ----
        public Dictionary<string, SalePerfTotalDto> RowTotals { get; set; } = new();  // key = MaterialName
        public Dictionary<int, SalePerfTotalDto> ColTotals { get; set; } = new();      // key = MonthId
        public SalePerfTotalDto GrandTotal { get; set; } = new();
    }
}
