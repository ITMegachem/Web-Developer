using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ICrossSellUpsellGainsService
    {
        Task<CrossSellUpsellGainsDto> GetAsync(CrossSellUpsellGainsFilter filter, CancellationToken ct = default);
    }
}
