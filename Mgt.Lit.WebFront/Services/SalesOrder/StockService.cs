using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Models.SalesOrder;
using Mgt.Lit.WebFront.Services.Member;
using Mgt.Lit.WebFront.Services.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class StockService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly AuthState _auth;
    private readonly PageContext _pageContext;

    public StockService(HttpClient http, AuthState auth, PageContext pageContext)
    {
        _http = http;
        _auth = auth;
        _pageContext = pageContext;
    }

    public async Task<StockRequirementResponse?> GetStockRequirement(
        string? plant,
        string? material,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedPost(
            "api/MGT_SalesOrder/StockMovement",
            new StockRequirementRequest
            {
                Plant = plant ?? string.Empty,
                Material = material ?? string.Empty
            });

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<StockRequirementResponse>(response, cancellationToken);
    }

    public async Task<MaterialStockApiResponse?> GetWarehouseStockCostByBatch(
        string? plant,
        string? materialGroup,
        string? materialCode,
        string? batchCode,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedPost(
            "api/MGT_SalesOrder/StockofMaterial",
            new WarehouseStockSearchRequest
            {
                Plant = plant ?? string.Empty,
                MaterialGroup = materialGroup ?? string.Empty,
                Material = materialCode ?? string.Empty,
                Batch = batchCode ?? string.Empty,
                Page = 1,
                PageSize = 9999
            });

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<MaterialStockApiResponse>(response, cancellationToken);
    }

    public async Task<List<MaterialConsumptionDto>?> GetMaterialConsumption(
        string? material,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedPost(
            "api/MGT_SalesOrder/MaterialConsumption",
            new MaterialConsumptionRequest
            {
                Material = material ?? string.Empty
            });

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<List<MaterialConsumptionDto>>(response, cancellationToken);
    }

    private HttpRequestMessage CreateAuthorizedPost<TRequest>(string url, TRequest body)
    {
        EnsureAuthenticated();

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);

        if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
            request.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);

        if (!string.IsNullOrWhiteSpace(_pageContext.Page))
            request.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

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

    private sealed class StockRequirementRequest
    {
        [JsonPropertyName("Plant")]
        public string Plant { get; set; } = string.Empty;

        [JsonPropertyName("Material")]
        public string Material { get; set; } = string.Empty;
    }

    private sealed class WarehouseStockSearchRequest
    {
        [JsonPropertyName("Plant")]
        public string Plant { get; set; } = string.Empty;

        [JsonPropertyName("MaterialGroup")]
        public string MaterialGroup { get; set; } = string.Empty;

        [JsonPropertyName("Material")]
        public string Material { get; set; } = string.Empty;

        [JsonPropertyName("Batch")]
        public string Batch { get; set; } = string.Empty;

        [JsonPropertyName("Page")]
        public int Page { get; set; }

        [JsonPropertyName("PageSize")]
        public int PageSize { get; set; }
    }

    private sealed class MaterialConsumptionRequest
    {
        [JsonPropertyName("Material")]
        public string Material { get; set; } = string.Empty;
    }
    public async Task<List<MaterialLookup>?> SearchMaterialForStockMovement(
    string keyword,
    CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/MGT_SalesOrder/MaterialLookup?keyword={Uri.EscapeDataString(keyword)}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);

        if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
            request.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);

        if (!string.IsNullOrWhiteSpace(_pageContext.Page))
            request.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<List<MaterialLookup>>(response, cancellationToken);
    }
    public async Task<List<MaterialLookup>> SearchMaterialAsync(
    string keyword,
    CancellationToken cancellationToken = default)
    {
        return await SearchMaterialForStockMovement(keyword, cancellationToken)
               ?? new List<MaterialLookup>();
    }
}