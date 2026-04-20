using Mgt.Lit.WebFront.Models.SalesOrder;
using static Mgt.Lit.WebFront.Pages.NofReport;

namespace Mgt.Lit.WebFront.Services
{
    public interface INofReportService
    {
        Task<PagedResult<NofReportRowDto>> GetNofReportAsync(
            NofReportFilter filter,
            CancellationToken cancellationToken = default);

       

        Task<List<ProductGroupLookup>> SearchProductGroupAsync(string keyword);
    }
}
