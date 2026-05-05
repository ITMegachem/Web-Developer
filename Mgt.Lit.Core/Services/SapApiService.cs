using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;

public class SapService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SapService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetStockRequirementAsync(string material, string plant)
    {
        var baseUrl = _configuration["SapConfig:StockRequirement:BaseUrl"];
        var authHeader = _configuration["SapConfig:StockRequirement:AuthHeader"];

        return await CallSapAsync(
            baseUrl!,
            authHeader!,
            "ZC_MMI101_STOCK_REQ/com.sap.gateway.srvd_a2x.zsd_mmi101_stock_req.v0001.GetStockRequirements",
            new
            {
                Material_Code = material,
                Plant = plant,
                _Detail = new List<object>()
            });
    }

    public async Task<string> GetWarehouseStockCostByBatchAsync(string material, string batch)
    {
        var baseUrl = _configuration["SapConfig:WarehouseStock:BaseUrl"];
        var authHeader = _configuration["SapConfig:WarehouseStock:AuthHeader"];

        return await CallSapAsync(
            baseUrl!,
            authHeader!,
            "ZC_MMI102_WAREHOUSE_STOCK/com.sap.gateway.srvd_a2x.zsd_mmi102_warehouse_stock.v0001.GetCostbyBatch",
            new
            {
                Material_Code = material,
                Batch_Code = batch ?? "",
                _Detail = new List<object>()
            });
    }

    private async Task<string> CallSapAsync(
        string baseUrl,
        string authHeader,
        string functionPath,
        object body)
    {
        // สร้าง handler เพื่อรองรับ Cookie (SAP ต้องใช้)
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };

        // ใช้ factory แต่กำหนด handler เอง
        var client = new HttpClient(handler);

        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // ==========================
        // STEP 1: FETCH CSRF TOKEN
        // ==========================
        var fetchRequest = new HttpRequestMessage(HttpMethod.Get, baseUrl);
        fetchRequest.Headers.Add("x-csrf-token", "Fetch");

        var fetchResponse = await client.SendAsync(fetchRequest);

        if (!fetchResponse.Headers.TryGetValues("x-csrf-token", out var tokenValues))
            throw new Exception("SAP did not return CSRF token.");

        var csrfToken = tokenValues.First();

        // ==========================
        // STEP 2: POST
        // ==========================
        var postUrl = $"{baseUrl}{functionPath}";

        var postRequest = new HttpRequestMessage(HttpMethod.Post, postUrl);
        postRequest.Headers.Add("x-csrf-token", csrfToken);

        postRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var postResponse = await client.SendAsync(postRequest);

        if (!postResponse.IsSuccessStatusCode)
        {
            var error = await postResponse.Content.ReadAsStringAsync();
            throw new Exception($"SAP Error: {error}");
        }

        return await postResponse.Content.ReadAsStringAsync();
    }
    public async Task<string> GetSalesOrderByIdAsync(string salesOrder)
    {
        var baseUrl = _configuration["SapConfig:SalesOrder:BaseUrl"];
        var authHeader = _configuration["SapConfig:SalesOrder:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // ✅ ใช้ salesOrder parameter จริงๆ ไม่ใช่ $top=1
        var url = $"{baseUrl.TrimEnd('/')}/A_SalesOrder('{salesOrder}')?$format=json";

        Console.WriteLine($"[DEBUG] URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception(result);

        return result;
    }
    public async Task<string> GetSalesOrderItemAsync(string salesOrder)
    {
        var baseUrl = _configuration["SapConfig:SalesOrder:BaseUrl"];
        var authHeader = _configuration["SapConfig:SalesOrder:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // filter เฉพาะ Item ของ SalesOrder นี้
        var url = $"{baseUrl.TrimEnd('/')}/A_SalesOrderItem" +
                  $"?$filter=SalesOrder eq '{salesOrder}'" +
                  $"&$format=json";

        Console.WriteLine($"[DEBUG] SAP Item URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception(result);

        return result;
    }
    public async Task<string> GetSalesOrderFullAsync(string salesOrder)
    {
        var baseUrl = _configuration["SapConfig:SalesOrder:BaseUrl"];
        var authHeader = _configuration["SapConfig:SalesOrder:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var url = $"{baseUrl.TrimEnd('/')}/A_SalesOrder('{salesOrder}')" +
                  $"?$expand=to_Item,to_Partner" +
                  $"&$format=json";

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception(result);

        return result;
    }

    public async Task<List<(string SalesOrder, string Material, decimal? OrderQuantity, string RequestedQuantityUnit, string PurchaseOrderByCustomer)>>
GetSalesOrderItemsAsync(List<string> salesOrders)
    {
        var baseUrl = _configuration["SapConfig:SalesOrder:BaseUrl"];
        var authHeader = _configuration["SapConfig:SalesOrder:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var result = new List<(string, string, decimal?, string, string)>();

        try
        {
            var soFilter = string.Join(" or ", salesOrders.Select(s => $"SalesOrder eq '{s}'"));
            var url = $"{baseUrl.TrimEnd('/')}/A_SalesOrderItem" +
                      $"?$filter={soFilter}" +
                      $"&$select=SalesOrder,Material,RequestedQuantity,RequestedQuantityUnit,PurchaseOrderByCustomer" +
                      $"&$format=json";

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return result;

            var json = JsonDocument.Parse(body);
            var items = json.RootElement
                .GetProperty("d")
                .GetProperty("results")
                .EnumerateArray();

            foreach (var item in items)
            {
                var so = item.GetProperty("SalesOrder").GetString() ?? "";
                var material = item.GetProperty("Material").GetString() ?? "";

                decimal? qty = null;
                if (item.TryGetProperty("RequestedQuantity", out var qProp) &&
                    decimal.TryParse(qProp.GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var qVal))
                    qty = qVal;

                var unit = item.TryGetProperty("RequestedQuantityUnit", out var uProp)
                    ? uProp.GetString() ?? "" : "";

                var purchaseOrder = item.TryGetProperty("PurchaseOrderByCustomer", out var poProp)
                    ? poProp.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(so) && !string.IsNullOrEmpty(material))
                    result.Add((so, material, qty, unit, purchaseOrder));
            }
        }
        catch { }

        return result;
    }
    public async Task<string> GetOutboundDeliveryItemsAsync(
    string? deliveryDocument = null,
    string? material = null,
    string? plant = null,
    int top = 100,
    int skip = 0)
    {
        var baseUrl = _configuration["SapConfig:OutboundDelivery:BaseUrl"];
        var authHeader = _configuration["SapConfig:OutboundDelivery:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(deliveryDocument))
            filters.Add($"DeliveryDocument eq '{deliveryDocument.Trim()}'");
        if (!string.IsNullOrWhiteSpace(material))
            filters.Add($"Material eq '{material.Trim()}'");
        if (!string.IsNullOrWhiteSpace(plant))
            filters.Add($"Plant eq '{plant.Trim()}'");

        var url = $"{baseUrl!.TrimEnd('/')}/A_OutbDeliveryItem" +
                  $"?$top={top}&$skip={skip}&$format=json";

        if (filters.Any())
            url += $"&$filter={string.Join(" and ", filters)}";

        Console.WriteLine($"[DEBUG] OutboundDelivery URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP Error: {result}");

        return result;
    }
    public async Task<string> GetCustomerByIdAsync(string customerId)
    {
        var baseUrl = _configuration["SapConfig:BusinessPartner:BaseUrl"];
        var authHeader = _configuration["SapConfig:BusinessPartner:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var url = $"{baseUrl!.TrimEnd('/')}/A_Customer('{customerId.Trim()}')";

        Console.WriteLine($"[DEBUG] Customer URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP Error: {result}");

        return result;
    }

    public async Task<string> GetCustomerListAsync(
        string? keyword = null,
        int top = 50,
        int skip = 0)
    {
        var baseUrl = _configuration["SapConfig:BusinessPartner:BaseUrl"];
        var authHeader = _configuration["SapConfig:BusinessPartner:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            filters.Add(
                $"(substringof('{k}', Customer) or " +
                $"substringof('{k}', CustomerName) or " +
                $"substringof('{k}', CustomerFullName))");
        }

        var url = $"{baseUrl!.TrimEnd('/')}/A_Customer" +
                  $"?$top={top}&$skip={skip}&$format=json" +
                  $"&$select=Customer,CustomerName,CustomerFullName,CustomerAccountGroup," +
                  $"TaxNumber3,CustomerCorporateGroup,DeletionIndicator";

        if (filters.Any())
            url += $"&$filter={string.Join(" and ", filters)}";

        Console.WriteLine($"[DEBUG] CustomerList URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP Error: {result}");

        return result;
    }
    public async Task<string> GetOutboundDeliveryItemsByFilterAsync(
     DateTime? dateFrom = null,
     DateTime? dateTo = null,
     string? plant = null,
     int top = 100,
     int skip = 0)
    {
        var baseUrl = _configuration["SapConfig:OutboundDelivery:BaseUrl"];
        var authHeader = _configuration["SapConfig:OutboundDelivery:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filters = new List<string>();

        if (dateFrom.HasValue)
            filters.Add($"ProductAvailabilityDate ge datetime'{dateFrom.Value:yyyy-MM-dd}T00:00:00'");
        if (dateTo.HasValue)
            filters.Add($"ProductAvailabilityDate le datetime'{dateTo.Value:yyyy-MM-dd}T23:59:59'");
        if (!string.IsNullOrWhiteSpace(plant))
            filters.Add($"Plant eq '{plant.Trim()}'");

        var url = $"{baseUrl!.TrimEnd('/')}/A_OutbDeliveryItem" +
                  $"?$top={top}&$skip={skip}&$format=json" +
                  $"&$select=DeliveryDocument,DeliveryDocumentItem,Material," +
                  $"DeliveryDocumentItemText,Plant,Batch,ActualDeliveryQuantity," +
                  $"DeliveryQuantityUnit,ProductAvailabilityDate," +
                  $"ReferenceSDDocument,GoodsMovementStatus,SDProcessStatus," +
                  $"SalesOffice,SalesGroup"; // ← ใช้ SalesOffice แทน SoldToParty

        if (filters.Any())
            url += $"&$filter={string.Join(" and ", filters)}";

        Console.WriteLine($"[DEBUG] OutboundDelivery Report URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP Error: {result}");

        return result;
    }
}