using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IPricingMarginPerformanceService
    {
        Task<PricingMarginPerformanceDto> GetAsync(PricingMarginPerformanceFilter filter, CancellationToken ct = default);
    }
}
