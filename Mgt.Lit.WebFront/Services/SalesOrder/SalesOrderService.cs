using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Constants;
using Mgt.Lit.WebFront.Models.SalesOrder;
using Mgt.Lit.WebFront.Services.Models;
using Mgt.Lit.WebFront.Services.SalesOrder;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SalesOrderService : ISalesOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly AuthState _auth;

    public SalesOrderService(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    public async Task<PagedResult<SalesOrderDto>> GetSalesOrdersAsync(
        SalesOrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedPost(
            ApiRoutes.SalesOrder.Search,
            new SalesOrderSearchRequest
            {
                SalesOrganization = filter.SalesOrganization,
                BillingDocument = filter.Document,
                DocDateFrom = filter.DocDateFrom,
                DocDateTo = filter.DocDateTo,
                SoldToParty = filter.SoldToParty,
                Material = filter.Material,
                Page = filter.Page,
                PageSize = filter.PageSize
            });

        using var response = await _http.SendAsync(request, cancellationToken);

        var apiResult = await ReadResponseAsync<ApiPagedResponse>(response, cancellationToken)
                        ?? new ApiPagedResponse();

        return new PagedResult<SalesOrderDto>
        {
            Items = apiResult.Items?
                .Select(x => new SalesOrderDto
                {
                    BillingDocument = x.BillingDocument ?? string.Empty,
                    DocDate = x.BillingDocumentDate,
                    SalesOrderDocument = x.SalesOrderDocument ?? string.Empty,
                    SoldTo = x.SoldToParty ?? string.Empty,
                    SoldToDescription = x.SoldToName ?? string.Empty,
                    SoldToMappingAddress = x.SoldToMappingAddress ?? string.Empty,  // ✅ เพิ่ม
                    ShipTo = x.ShiptoCode ?? string.Empty,
                    ShipToDescription = x.ShipToName ?? string.Empty,
                    ShipToMappingAddress = x.ShipToMappingAddress ?? string.Empty,  // ✅ เพิ่ม
                    MaterialCode = x.Material ?? string.Empty,
                    MaterialDescription = x.MaterialName ?? string.Empty,
                    SalesEmployee = x.SalesEmployee ?? string.Empty
                })
                .ToList() ?? new List<SalesOrderDto>(),
            TotalItems = apiResult.TotalCount
        };
    }

    public async Task<List<SoldToLookupDto>> SearchSoldToAsync(
    string keyword,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<SoldToLookupDto>();

        var result = await GetSalesOrdersAsync(new SalesOrderFilter
        {
            SoldToParty = keyword.Trim(),
            Page = 1,
            PageSize = 10
        }, cancellationToken);

        return result.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.SoldTo))
            .Select(x => new SoldToLookupDto
            {
                Code = x.SoldTo,
                Name = x.SoldToDescription
            })
            .DistinctBy(x => x.Code)
            .ToList();
    }

    public async Task<List<MaterialLookup>> SearchMaterialAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<MaterialLookup>();

        var result = await GetSalesOrdersAsync(new SalesOrderFilter
        {
            Material = keyword.Trim(),
            Page = 1,
            PageSize = 10
        }, cancellationToken);

        return result.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.MaterialCode))
            .Select(x => new MaterialLookup
            {
                Code = x.MaterialCode,
                Name = x.MaterialDescription
            })
            .DistinctBy(x => x.Code)
            .ToList();
    }

    private HttpRequestMessage CreateAuthorizedPost<TRequest>(string url, TRequest body)
    {
        EnsureAuthenticated();

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        request.Headers.TryAddWithoutValidation("X-Menu", "Sales");
        request.Headers.TryAddWithoutValidation("X-Page", "Sales List");

        return request;
    }

    private void EnsureAuthenticated()
    {
        if (string.IsNullOrWhiteSpace(_auth.Token))
            throw new UnauthorizedAccessException("Token is missing");
    }

    private static async Task<T?> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException();

        response.EnsureSuccessStatusCode();

        if (response.Content is null)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private sealed class SalesOrderSearchRequest
    {
        [JsonPropertyName("SalesOrganization")]
        public string? SalesOrganization { get; set; }

        [JsonPropertyName("BillingDocument")]
        public string? BillingDocument { get; set; }

        [JsonPropertyName("DocDateFrom")]
        public DateTime? DocDateFrom { get; set; }

        [JsonPropertyName("DocDateTo")]
        public DateTime? DocDateTo { get; set; }

        [JsonPropertyName("SoldToParty")]
        public string? SoldToParty { get; set; }

        [JsonPropertyName("Material")]
        public string? Material { get; set; }

        [JsonPropertyName("Page")]
        public int Page { get; set; }

        [JsonPropertyName("PageSize")]
        public int PageSize { get; set; }
    }
}