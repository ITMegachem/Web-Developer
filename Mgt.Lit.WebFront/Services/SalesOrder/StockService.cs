using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Models.SalesOrder;
using Mgt.Lit.WebFront.Services.Member;
using Mgt.Lit.WebFront.Services.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Mgt.Lit.WebFront.Pages.ManageOutboubDeliveries;

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
    public async Task<string> GetMaterialDescriptionAsync(
    string code,
    CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/MGT_SalesOrder/MaterialDescription?code={Uri.EscapeDataString(code)}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);

        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) return "";

        var result = await response.Content
            .ReadFromJsonAsync<MaterialDescriptionResult>(JsonOptions, cancellationToken);

        return result?.Description ?? "";
    }

    private sealed class MaterialDescriptionResult
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";
    }

    private void EnsureAuthenticated()
    {
        if (string.IsNullOrWhiteSpace(_auth.Token))
            throw new UnauthorizedAccessException("Token is missing");
    }

    public async Task<string> GetProductDescriptionFromSapAsync(
    string material,
    CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureAuthenticated();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/MGT_SalesOrder/ProductDescription" +
                $"?material={Uri.EscapeDataString(material)}&language=EN");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth.Token);

            using var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode) return "";

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            return doc.RootElement.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? ""
                : "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetProductDescriptionFromSapAsync: {ex.Message}");
            return "";
        }
    }

    public async Task<OutboundDeliveryResponse?> GetOutboundDeliveriesAsync(
     OutboundDeliveryReportRequest? request = null,
     CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var body = request ?? new OutboundDeliveryReportRequest();

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "api/MGT_SalesOrder/OutboundDeliveryReport")
        {
            Content = JsonContent.Create(body)
        };

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);

        if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
            req.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);
        if (!string.IsNullOrWhiteSpace(_pageContext.Page))
            req.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

        using var response = await _http.SendAsync(req, cancellationToken);
        return await ReadResponseAsync<OutboundDeliveryResponse>(response, cancellationToken);
    }
    // ── SoldTo Lookup ─────────────────────────────────────────────────────────
    public sealed class SoldToLookup
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    public async Task<List<SoldToLookup>?> SearchSoldToAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/MGT_SalesOrder/SoldToLookup?keyword={Uri.EscapeDataString(keyword)}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);

        if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
            request.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);
        if (!string.IsNullOrWhiteSpace(_pageContext.Page))
            request.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<List<SoldToLookup>>(response, cancellationToken);
    }
    private sealed class StockRequirementRequest
    {
        [JsonPropertyName("Plant")]
        public string Plant { get; set; } = string.Empty;

        [JsonPropertyName("Material")]
        public string Material { get; set; } = string.Empty;
    }
    public sealed class OutboundDeliveryReportRequest
    {
        [JsonPropertyName("deliveryDateFrom")]
        public DateTime? DeliveryDateFrom { get; set; }

        [JsonPropertyName("deliveryDateTo")]
        public DateTime? DeliveryDateTo { get; set; }
        [JsonPropertyName("documentDateFrom")]
        public DateTime? DocumentDateFrom { get; set; }   // ✅ เปลี่ยน

        [JsonPropertyName("documentDateTo")]
        public DateTime? DocumentDateTo { get; set; }     // ✅ เพิ่ม

        [JsonPropertyName("customerName")]
        public string? CustomerName { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }

        [JsonPropertyName("routeName")]
        public string? RouteName { get; set; }

        [JsonPropertyName("plant")]
        public string? Plant { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; } = 1;

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; } = 100;
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
    // ── Request DTO ──────────────────────────────────────────────────────────
    public sealed class SalesReportRequest
    {
        [JsonPropertyName("page")]
        public int Page { get; set; } = 1;

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; } = 100;

        [JsonPropertyName("billingDateFrom")]
        public DateTime? BillingDateFrom { get; set; }

        [JsonPropertyName("billingDateTo")]
        public DateTime? BillingDateTo { get; set; }

        [JsonPropertyName("soldToParty")]
        public string? SoldToParty { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }

        [JsonPropertyName("salesGroup")]
        public string? SalesGroup { get; set; }

        [JsonPropertyName("productGroup")]
        public string? ProductGroup { get; set; }
        [JsonPropertyName("deliveryDateFrom")]
        public DateTime? DeliveryDateFrom { get; set; }  // ✅ เพิ่ม

        [JsonPropertyName("deliveryDateTo")]
        public DateTime? DeliveryDateTo { get; set; }  // ✅ เพิ่ม
    }

    // ── Response DTO ─────────────────────────────────────────────────────────
    public sealed class SalesReportDto
    {
        public string? BillingDocument { get; set; }
        public DateTime? BillingDocumentDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string? SoldToParty { get; set; }
        public string? CustomerFullName { get; set; }
        public string? TransactionCurrency { get; set; }
        public string? ReferenceSDDocument { get; set; }
        public string? SalesGroup { get; set; }
        public string? Material { get; set; }
        public string? MaterialName { get; set; }
        public string? MaterialGroup { get; set; }
        public string? SalesEmployeeBP { get; set; }
        public string? SoldToName { get; set; }
        public string? SoldToAddress { get; set; }
        public decimal? NetAmount { get; set; }
        public decimal? CostAmount { get; set; }
        public decimal? GrossProfit { get; set; }
        public double? Quantity { get; set; }
        public string? Unit { get; set; }
        // จาก View_ProductLastPrice
        public double? LastPricePerPack { get; set; }
        public double? LastPrice_PerKG { get; set; }
        public double? CostPerPack { get; set; }
        public double? CostPerKG { get; set; }
        public DateTime? LastSaleDate { get; set; }
    }

    public sealed class SalesReportResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<SalesReportDto> Items { get; set; } = new();
    }

    // ── Method ───────────────────────────────────────────────────────────────
    public async Task<SalesReportResponse?> GetSalesReportAsync(
        SalesReportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var body = request ?? new SalesReportRequest();

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "api/MGT_SalesOrder/SalesReport")
        {
            Content = JsonContent.Create(body)
        };

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);

        if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
            req.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);
        if (!string.IsNullOrWhiteSpace(_pageContext.Page))
            req.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

        using var response = await _http.SendAsync(req, cancellationToken);
        return await ReadResponseAsync<SalesReportResponse>(response, cancellationToken);
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