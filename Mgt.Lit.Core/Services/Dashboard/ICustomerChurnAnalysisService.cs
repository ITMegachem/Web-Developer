using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ICustomerChurnAnalysisService
    {
        Task<CustomerChurnAnalysisDto> GetAsync(CustomerChurnAnalysisFilter filter, CancellationToken ct = default);
    }
}
