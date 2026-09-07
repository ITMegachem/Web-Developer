using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IOpportunityWinRateService
    {
        Task<OpportunityWinRateDto> GetAsync(OpportunityWinRateFilter filter, CancellationToken ct = default);
    }
}
