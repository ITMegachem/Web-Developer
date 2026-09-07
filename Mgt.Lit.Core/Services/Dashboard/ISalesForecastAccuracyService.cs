using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ISalesForecastAccuracyService
    {
        Task<SalesForecastAccuracyDto> GetAsync(SalesForecastAccuracyFilter filter, CancellationToken ct = default);
    }
}
