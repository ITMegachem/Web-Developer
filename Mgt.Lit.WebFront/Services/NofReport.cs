using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Models.SalesOrder;
using Mgt.Lit.WebFront.Services;
using Mgt.Lit.WebFront.Services.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NofReportService : INofReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly AuthState _auth;

    public NofReportService(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    public async Task<PagedResult<NofReportRowDto>> GetNofReportAsync(
        NofReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedPost(
            "api/MGT_SalesOrder/nofreport",
            new NofReportSearchRequest
            {
                Material = filter.Material,
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Page = filter.Page,
                PageSize = filter.PageSize
            });

        using var response = await _http.SendAsync(request, cancellationToken);

        var apiResult = await ReadResponseAsync<NofApiPagedResponse>(response, cancellationToken)
                        ?? new NofApiPagedResponse();

        // จัด group items → MonthlyQty dict
        var rows = apiResult.Items?
            .GroupBy(x => new { x.Material, x.MaterialName, x.SoldToParty, x.SoldToName })
            .Select(g =>
            {
                var monthlyQty = new Dictionary<string, decimal>();

                foreach (var item in g)
                {
                    if (item.BillingDocumentDate.HasValue)
                    {
                        var key = item.BillingDocumentDate.Value.ToString("yyyy/MM");
                        monthlyQty.TryGetValue(key, out var existing);

                        // ✅ เปลี่ยนจาก Quantity → QuantityKG
                        var value = item.QuantityKG ?? item.Quantity ?? 0;
                        monthlyQty[key] = existing + value;
                    }
                }

                return new NofReportRowDto
                {
                    Material = g.Key.Material,
                    MaterialName = g.Key.MaterialName,
                    SoldToParty = g.Key.SoldToParty,
                    SoldToName = g.Key.SoldToName,
                    MonthlyQty = monthlyQty
                };
            })
            .ToList() ?? new List<NofReportRowDto>();

        return new PagedResult<NofReportRowDto>
        {
            Items = rows,
            TotalItems = apiResult.TotalCount
        };
    }

    public async Task<List<MaterialLookup>> SearchMaterialAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<MaterialLookup>();

        var result = await GetNofReportAsync(new NofReportFilter
        {
            Material = keyword.Trim(),
            Page = 1,
            PageSize = 10
        }, cancellationToken);

        return result.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Material))
            .Select(x => new MaterialLookup
            {
                Code = x.Material!,
                Name = x.MaterialName ?? ""
            })
            .DistinctBy(x => x.Code)
            .ToList();
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private HttpRequestMessage CreateAuthorizedPost<TRequest>(string url, TRequest body)
    {
        EnsureAuthenticated();

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _auth.Token);
        request.Headers.TryAddWithoutValidation("X-Menu", "Sales");
        request.Headers.TryAddWithoutValidation("X-Page", "NOF Report");

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

        return await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    // ── Inner Request/Response Models ─────────────────────────────────────────

    private sealed class NofReportSearchRequest
    {
        [JsonPropertyName("material")]
        public string? Material { get; set; }

        [JsonPropertyName("dateFrom")]
        public DateTime? DateFrom { get; set; }

        [JsonPropertyName("dateTo")]
        public DateTime? DateTo { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }
    }

    private sealed class NofApiPagedResponse
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("items")]
        public List<NofApiItem>? Items { get; set; }
    }

    private sealed class NofApiItem
    {
        [JsonPropertyName("billingDocument")]
        public string? BillingDocument { get; set; }

        [JsonPropertyName("billingDocumentDate")]
        public DateTime? BillingDocumentDate { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }

        [JsonPropertyName("materialName")]
        public string? MaterialName { get; set; }

        [JsonPropertyName("soldToParty")]
        public string? SoldToParty { get; set; }

        [JsonPropertyName("soldToName")]
        public string? SoldToName { get; set; }

        [JsonPropertyName("quantity")]
        public decimal? Quantity { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }
        [JsonPropertyName("quantityKG")]   // ✅ เพิ่ม
        public decimal? QuantityKG { get; set; }
    }
}