using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ISalesProductivityService
    {
        Task<SalesProductivityDto> GetAsync(SalesProductivityFilter filter, CancellationToken ct = default);
    }
}
