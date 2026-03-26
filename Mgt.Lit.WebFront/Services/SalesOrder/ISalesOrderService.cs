using Mgt.Lit.WebFront.Models.SalesOrder;

namespace Mgt.Lit.WebFront.Services.SalesOrder
{
    public interface ISalesOrderService
    {
        Task<PagedResult<SalesOrderDto>> GetSalesOrdersAsync(
            SalesOrderFilter filter,
            CancellationToken cancellationToken = default);

        Task<List<SoldToLookupDto>> SearchSoldToAsync(
            string keyword,
            CancellationToken cancellationToken = default);

        Task<List<MaterialLookup>> SearchMaterialAsync(
            string keyword,
            CancellationToken cancellationToken = default);
    }
}