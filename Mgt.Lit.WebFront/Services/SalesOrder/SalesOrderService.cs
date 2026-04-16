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
        Document          = filter.Document,  // ← Document ไม่ใช่ BillingDocument
        DeliveryDateFrom  = filter.DocDateFrom,
        DeliveryDateTo    = filter.DocDateTo,
        SoldToParty       = filter.SoldToParty,
        Material          = filter.Material,
        Page              = filter.Page,
        PageSize          = filter.PageSize
    });

        using var response = await _http.SendAsync(request, cancellationToken);

        var apiResult = await ReadResponseAsync<ApiPagedResponse>(response, cancellationToken)
                        ?? new ApiPagedResponse();

        return new PagedResult<SalesOrderDto>
        {
            Items = apiResult.Items?
    .Select(x => new SalesOrderDto
    {
        BillingDocument = x.BillingDocument ?? "",
        DocDate = x.DocDate ?? x.BillingDocumentDate ?? DateTime.MinValue,  // ← BillingDocumentDate
        DeliveryDate = x.DeliveryDate,
        SalesOrderDocument = x.SalesOrderDocument ?? "",
        SoldTo = x.SoldToParty ?? "",
        SoldToDescription = x.SoldToName ?? "",
        SoldToMappingAddress = x.SoldToAddress ?? "",   // ← SoldToAddress
        ShipTo = x.ShiptoCode ?? "",
        ShipToDescription = x.ShipToName ?? "",
        ShipToMappingAddress = x.ShipToAddress ?? "",   // ← ShipToAddress
        MaterialCode = x.Material ?? "",
        MaterialDescription = x.MaterialName ?? "",
        SalesEmployee = x.SalesEmployee ?? ""
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

        EnsureAuthenticated();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/MGT_SalesOrder/SoldToLookup?keyword={Uri.EscapeDataString(keyword.Trim())}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        request.Headers.TryAddWithoutValidation("X-Menu", "Sales");
        request.Headers.TryAddWithoutValidation("X-Page", "Sales List");

        using var response = await _http.SendAsync(request, cancellationToken);

        return await ReadResponseAsync<List<SoldToLookupDto>>(response, cancellationToken)
               ?? new List<SoldToLookupDto>();
    }

    public async Task<List<MaterialLookup>> SearchMaterialAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<MaterialLookup>();

        EnsureAuthenticated();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/MGT_SalesOrder/MaterialLookup?keyword={Uri.EscapeDataString(keyword.Trim())}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        request.Headers.TryAddWithoutValidation("X-Menu", "Sales");
        request.Headers.TryAddWithoutValidation("X-Page", "Sales List");

        using var response = await _http.SendAsync(request, cancellationToken);

        return await ReadResponseAsync<List<MaterialLookup>>(response, cancellationToken)
               ?? new List<MaterialLookup>();
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
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Session expired. Please login again.");

        response.EnsureSuccessStatusCode();

        if (response.Content is null)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
    private sealed class SalesOrderSearchRequest
    {
        [JsonPropertyName("SalesOrganization")]
        public string? SalesOrganization { get; set; }

        [JsonPropertyName("Document")]
        public string? Document { get; set; }

        // ลบ BillingDocument ออก ← 

        [JsonPropertyName("DeliveryDateFrom")]
        public DateTime? DeliveryDateFrom { get; set; }

        [JsonPropertyName("DeliveryDateTo")]
        public DateTime? DeliveryDateTo { get; set; }

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