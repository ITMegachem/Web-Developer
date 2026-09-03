using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ISalePerformanceService
    {
        Task<SalePerformanceDto> GetAsync(SalePerformanceFilter filter, CancellationToken ct = default);

        // รายชื่อ BU ทั้งหมดที่มีข้อมูล (ใช้ตอน user เป็น manager ใหญ่ที่ดูได้ทุก BU)
        Task<List<string>> GetDistinctBusAsync(CancellationToken ct = default);
    }
}
