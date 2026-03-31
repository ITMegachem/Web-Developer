using Mgt.Lit.WebFront.Models.SalesOrder;

namespace Mgt.Lit.WebFront.Services
{
    public interface INofReportService
    {
        Task<PagedResult<NofReportRowDto>> GetNofReportAsync(
            NofReportFilter filter,
            CancellationToken cancellationToken = default);

        Task<List<MaterialLookup>> SearchMaterialAsync(
            string keyword,
            CancellationToken cancellationToken = default);
    }
}
