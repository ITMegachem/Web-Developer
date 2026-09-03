using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public class YearlyComparisonService : IYearlyComparisonService
    {
        private readonly AppDbContext _db;

        public YearlyComparisonService(AppDbContext db) => _db = db;

        // จำนวนปีที่แสดง (ปีปัจจุบัน + ย้อนหลัง = 6 ปี) — อยากได้กี่ปีแก้ตรงนี้
        private const int YearsToShow = 6;

        public async Task<YearlyComparisonDto> GetAsync(CancellationToken ct = default)
        {
            int currentYear = DateTime.Now.Year;
            int startYear = currentYear - (YearsToShow - 1);   // เช่น 2026 -> 2021

            // รวม NetAmount/GrossProfit ต่อ (ปี, เดือน) ในช่วง startYear..currentYear
            // ดึงปี/เดือนจาก BillingDocumentDate (สอดคล้องกับหน้า Sales Overview)
            var raw = await _db.MGT_Sale.AsNoTracking()
                .Where(x => x.BillingDocumentDate.HasValue
                         && x.BillingDocumentDate.Value.Year >= startYear
                         && x.BillingDocumentDate.Value.Year <= currentYear)
                .GroupBy(x => new
                {
                    Y = x.BillingDocumentDate!.Value.Year,
                    M = x.BillingDocumentDate!.Value.Month
                })
                .Select(g => new
                {
                    g.Key.Y,
                    g.Key.M,
                    Net = g.Sum(x => x.NetAmount ?? 0),
                    Gp = g.Sum(x => x.GrossProfit ?? 0)
                })
                .ToListAsync(ct);

            var months = await _db.MsMonths.AsNoTracking()
                .OrderBy(m => m.MonthId)
                .ToListAsync(ct);

            var result = new YearlyComparisonDto();

            for (int y = startYear; y <= currentYear; y++)   // เรียงปีน้อย -> ปีมาก
            {
                var chart = new YearlyChartDto { Year = y };

                foreach (var m in months)
                {
                    var row = raw.FirstOrDefault(r => r.Y == y && r.M == m.MonthId);
                    var net = row?.Net ?? 0;
                    var gp = row?.Gp ?? 0;

                    chart.Months.Add(new YearMonthPointDto
                    {
                        MonthId = m.MonthId,
                        MonthName = m.MonthNameEN,
                        NetAmount = net,
                        GrossProfit = gp,
                        GrossProfitMarginPercent = net == 0 ? 0 : Math.Round(gp / net * 100, 2)
                    });
                }

                chart.TotalNetAmount = chart.Months.Sum(p => p.NetAmount);
                chart.TotalGrossProfit = chart.Months.Sum(p => p.GrossProfit);
                chart.GrossProfitMarginPercent = chart.TotalNetAmount == 0
                    ? 0
                    : Math.Round(chart.TotalGrossProfit / chart.TotalNetAmount * 100, 2);

                result.Years.Add(chart);
            }

            return result;
        }
    }
}