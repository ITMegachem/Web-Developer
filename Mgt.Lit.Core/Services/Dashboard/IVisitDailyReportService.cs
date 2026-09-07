using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IVisitDailyReportService
    {
        Task<VisitDailyReportDto> GetAsync(VisitDailyReportFilter filter, CancellationToken ct = default);
    }
}
