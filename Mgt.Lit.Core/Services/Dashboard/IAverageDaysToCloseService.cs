using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IAverageDaysToCloseService
    {
        Task<AverageDaysToCloseDto> GetAsync(AverageDaysToCloseFilter filter, CancellationToken ct = default);
    }
}
