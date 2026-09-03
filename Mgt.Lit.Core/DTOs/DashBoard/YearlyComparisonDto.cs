using System;
using System.Collections.Generic;
using System.Text;
namespace Mgt.Lit.Core.DTOs.DashBoard
{
    // ทั้งหน้า = หลายปีเรียงกัน (ปีน้อย -> ปีมาก)
    public class YearlyComparisonDto
    {
        public List<YearlyChartDto> Years { get; set; } = new();
    }
    // 1 จุดข้อมูล = 1 เดือน ของปีหนึ่ง
    public class YearMonthPointDto
    {
        public int MonthId { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal GrossProfitMarginPercent { get; set; }
    }

    // 1 ปี = 1 area chart (มี 12 เดือน)
    public class YearlyChartDto
    {
        public int Year { get; set; }
        public List<YearMonthPointDto> Months { get; set; } = new();

        // ยอดรวมทั้งปี (เผื่อโชว์บนหัวการ์ด)
        public decimal TotalNetAmount { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal GrossProfitMarginPercent { get; set; }
    }

    
}