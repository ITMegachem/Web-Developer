using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IProductOverviewService
    {
        Task<ProductOverviewDto> GetAsync(ProductOverviewFilter filter, CancellationToken ct = default);
    }
}