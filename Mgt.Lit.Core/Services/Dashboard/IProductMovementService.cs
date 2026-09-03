using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IProductMovementService
    {
        Task<ProductMovementDto> GetAsync(ProductMovementFilter filter, CancellationToken ct = default);
    }
}