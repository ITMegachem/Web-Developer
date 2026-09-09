namespace Mgt.Lit.Core.DTOs.DashBoard
{
    public class ProductMovementFilter
    {
        public int? Year { get; set; }
        public string? SalesGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }      // server บังคับ (ล็อกเป็นของตัวเองถ้า UserRole เป็นพนักงานขายทั่วไป)
        public string? CustomerFullName { get; set; }
        public string? MaterialName { get; set; }
        public int? MonthId { get; set; }              
    }

    public class ProductMovementRowDto
    {
        public string MaterialName { get; set; } = string.Empty;
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
    }

    public class ProductMovementDto
    {
        public List<ProductMovementRowDto> ByMonth { get; set; } = new();

        // ตัวเลือก dropdown (scope ตาม BU + Year)
        public List<string> Customers { get; set; } = new();  
        public List<string> Materials { get; set; } = new();   

        // ---- ค่ารวม (backend คำนวณ, Quantity) ----
        public Dictionary<string, decimal> RowTotals { get; set; } = new();
        public Dictionary<int, decimal> ColTotals { get; set; } = new();  
        public decimal GrandTotal { get; set; }
    }
}
