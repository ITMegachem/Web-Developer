using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ISalesOverviewService
    {
        Task<SalesOverviewDto> GetOverviewAsync(SalesOverviewFilter filter, CancellationToken ct = default);
    }
}
