using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IBillingDailyService
    {
        Task<BillingDailyDto> GetAsync(BillingDailyFilter filter, CancellationToken ct = default);
    }
}