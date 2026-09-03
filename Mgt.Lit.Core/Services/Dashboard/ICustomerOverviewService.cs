using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ICustomerOverviewService
    {
        Task<CustomerOverviewDto> GetAsync(CustomerOverviewFilter filter, CancellationToken ct = default);

        // รายชื่อ BU ทั้งหมด (ใช้ตอน user เป็น manager ใหญ่ที่ดูได้ทุก BU)
        Task<List<string>> GetDistinctBusAsync(CancellationToken ct = default);
    }
}
