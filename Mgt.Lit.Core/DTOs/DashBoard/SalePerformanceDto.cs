namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // filter 3 ค่า ทุกอย่าง optional (ไม่ส่ง = ไม่กรอง)
    public class SalePerformanceFilter
    {
        public int? Year { get; set; }
        public string? SalesGroup { get; set; }        // BU — server บังคับตาม permission
        public string? SalesEmployeeBP { get; set; }
    }

    // ตาราง 1: Total Revenue Overview By Sales — ต่อ (คน × ปี)
    public class SalePerfByYearRowDto
    {
        public string SaleEM { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    // ตาราง 2: รายได้ Sale แต่ละเดือน — flat rows (คน × เดือน) ฝั่งหน้าค่อย pivot เป็น matrix
    public class SalePerfByMonthRowDto
    {
        public string SaleEM { get; set; } = string.Empty;
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    // Chart 3: Revenue and GP Margin Trend — ต่อคน (X = Sale Employee)
    public class SalePerfEmployeeDto
    {
        public string SaleEM { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    // ค่ารวม (ใช้ทั้งแถว/คอลัมน์/grand total) — คำนวณฝั่ง backend
    public class SalePerfTotalDto
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal TotalPercentMargin { get; set; }
    }

    public class SalePerformanceDto
    {
        public List<SalePerfByYearRowDto> ByYear { get; set; } = new();     // ตาราง 1
        public List<SalePerfByMonthRowDto> ByMonth { get; set; } = new();   // ตาราง 2
        public List<SalePerfEmployeeDto> EmployeeTrend { get; set; } = new(); // chart 3

        // ---- BU scope (server เป็นคนกำหนด) ----
        // BU ที่ user คนนี้เลือกดูได้: BU1 (manager ใหญ่) = ทุก BU, ที่เหลือ = เฉพาะ BU ตัวเอง
        // ฝั่งหน้าโชว์ dropdown เฉพาะเมื่อมีมากกว่า 1 ค่า
        public List<string> AvailableBus { get; set; } = new();
        // BU ที่ server ใช้กรองจริงในรอบนี้ (ไว้ pre-select ให้ตรงกับที่ backend บังคับ)
        public string? EffectiveBu { get; set; }

        // ---- ค่ารวม (คำนวณจาก backend เป็น single source of truth) ----
        public Dictionary<string, SalePerfTotalDto> ByYearRowTotals { get; set; } = new();   // key = SaleEM
        public Dictionary<int, SalePerfTotalDto> ByYearColTotals { get; set; } = new();        // key = Year
        public SalePerfTotalDto ByYearGrandTotal { get; set; } = new();

        public Dictionary<string, SalePerfTotalDto> ByMonthRowTotals { get; set; } = new();   // key = SaleEM
        public Dictionary<int, SalePerfTotalDto> ByMonthColTotals { get; set; } = new();       // key = MonthId
        public SalePerfTotalDto ByMonthGrandTotal { get; set; } = new();
    }
}
