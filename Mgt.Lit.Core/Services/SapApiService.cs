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

    // Material Stock (On Hand Qty) — SAP standard API_MATERIAL_STOCK_SRV, entity A_MatlStkInAcctMod
    // ★ ใช้สำหรับ Forecast Monthly Report — ยังไม่เคยยืนยัน field name จริงกับ tenant นี้โดยตรง (ต่างจาก Zoho ที่เรียก
    //   live API ได้เอง) จึงลอง parse หลายชื่อ field ที่เป็นไปได้ใน GetOnHandQtyAsync ฝั่ง reporting service แทน
    public async Task<string> GetMaterialStockAsync(string material)
    {
        var baseUrl = _configuration["SapConfig:MaterialStock:BaseUrl"];
        var authHeader = _configuration["SapConfig:MaterialStock:AuthHeader"];

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var url = $"{baseUrl.TrimEnd('/')}/A_MatlStkInAcctMod?$filter=Material eq '{material}'&$format=json";

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception(result);

        return result;
    }

    // ★★★ Performance tuning (2026-09) ★★★ — Forecast Monthly Report เดิมเรียก GetMaterialStockAsync ทีละ Material Code
    // (พบจริงมากถึง ~70 ครั้งต่อการโหลด 1 ครั้ง แต่ละครั้งเปิด HttpClient ใหม่ + round trip ไป SAP แยกกัน) รวม batch
    // เป็นการยิง 1 request ต่อ material ~25 ตัว (OData รองรับ "or" ใน $filter) ลดจำนวน round trip ไป SAP ลงหลายสิบเท่า
    public async Task<string> GetMaterialStockBatchAsync(IEnumerable<string> materials)
    {
        var baseUrl = _configuration["SapConfig:MaterialStock:BaseUrl"];
        var authHeader = _configuration["SapConfig:MaterialStock:AuthHeader"];

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filter = string.Join(" or ", materials.Select(m => $"Material eq '{m}'"));
        var url = $"{baseUrl.TrimEnd('/')}/A_MatlStkInAcctMod?$filter={Uri.EscapeDataString(filter)}&$format=json";

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception(result);

        return result;
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
    public async Task<string> GetOutboundDeliveryHeadersAsync(List<string> deliveryDocuments)
    {
        var baseUrl = _configuration["SapConfig:OutboundDelivery:BaseUrl"];
        var authHeader = _configuration["SapConfig:OutboundDelivery:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // สร้าง filter จาก delivery doc numbers
        var filterParts = deliveryDocuments
            .Select(d => $"DeliveryDocument eq '{d}'");
        var filter = string.Join(" or ", filterParts);

        var url = $"{baseUrl!.TrimEnd('/')}/A_OutbDeliveryHeader" +
                  $"?$format=json" +
                  $"&$select=DeliveryDocument,SoldToParty,ShipToParty" +
                  $"&$filter={filter}";

        Console.WriteLine($"[DEBUG] DeliveryHeader URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP DeliveryHeader Error: {result}");

        return result;
    }
    public async Task<string> GetOutboundDeliveryItemsByFilterAsync(
     DateTime? dateFrom = null,
     DateTime? dateTo = null,
     DateTime? documentDateFrom = null,   // ✅ เปลี่ยน
    DateTime? documentDateTo = null,     // ✅ เพิ่ม
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
        if (documentDateFrom.HasValue)
            filters.Add($"CreationDate ge datetime'{documentDateFrom.Value:yyyy-MM-dd}T00:00:00'");
        if (documentDateTo.HasValue)
            filters.Add($"CreationDate le datetime'{documentDateTo.Value:yyyy-MM-dd}T23:59:59'");
        if (!string.IsNullOrWhiteSpace(plant))
            filters.Add($"Plant eq '{plant.Trim()}'");
        if (!filters.Any())
            return "{\"d\":{\"results\":[]}}";
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
    public async Task<string?> GetProductDescriptionAsync(string material, string language = "EN")
    {
        var baseUrl = _configuration["SapConfig:ProductMaster:BaseUrl"];
        var authHeader = _configuration["SapConfig:ProductMaster:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var url = $"{baseUrl!.TrimEnd('/')}/A_ProductDescription" +
                  $"?$filter=Product eq '{material}' and Language eq '{language}'" +
                  $"&$select=Product,Language,ProductDescription" +
                  $"&$format=json";

        Console.WriteLine($"[DEBUG] ProductDescription URL: {url}");

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = doc.RootElement
            .GetProperty("d")
            .GetProperty("results")
            .EnumerateArray()
            .ToList();

        return results.FirstOrDefault()
            .TryGetProperty("ProductDescription", out var desc)
            ? desc.GetString()
            : null;
    }
    public async Task<string> GetSalesOrderSoldToAsync(List<string> salesOrders)
    {
        var baseUrl = _configuration["SapConfig:SalesOrder:BaseUrl"];
        var authHeader = _configuration["SapConfig:SalesOrder:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filterParts = salesOrders.Select(so => $"SalesOrder eq '{so}'");
        var filter = string.Join(" or ", filterParts);

        var url = $"{baseUrl!.TrimEnd('/')}/A_SalesOrder" +
                  $"?$format=json" +
                  $"&$select=SalesOrder,SoldToParty" +
                  $"&$filter={filter}";

        Console.WriteLine($"[DEBUG] SAP SO SoldTo URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP SO Error: {result}");

        return result;
    }
    public async Task<string> GetProductMasterAsync(IEnumerable<string> materialCodes)
    {
        var baseUrl = _configuration["SapConfig:ProductMaster:BaseUrl"];
        var authHeader = _configuration["SapConfig:ProductMaster:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var codeList = materialCodes.ToList();
        var filter = string.Join(" or ", codeList.Select(m => $"Product eq '{m}'"));

        // ✅ ใช้ชื่อ field จริงจาก SAP response
        var url = $"{baseUrl!.TrimEnd('/')}/A_Product" +
           $"?$filter={filter}" +
           $"&$select=Product,NetWeight,GrossWeight,WeightUnit," +
           $"YY1_IMPORTLICENSE_PRD,YY1_MM_TRANSPORTCLASS_PRD,YY1_MM_STORAGECLASS_PRD" +
           $"&$format=json";

        Console.WriteLine($"[DEBUG] ProductMaster URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[DEBUG] ProductMaster RAW: {result}");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP ProductMaster Error: {result}");

        return result;
    }

    public async Task<string> GetProductClassificationAsync(IEnumerable<string> materialCodes)
    {
        // A_ProductPlantMRPArea or A_ProductDescription — Class No. from A_ProductValuationAccount
        // Class No. typically lives in A_ProductClassification
        var baseUrl = _configuration["SapConfig:ProductMaster:BaseUrl"];
        var authHeader = _configuration["SapConfig:ProductMaster:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var filter = string.Join(" or ", materialCodes.Select(m => $"Product eq '{m}'"));

        var url = $"{baseUrl!.TrimEnd('/')}/A_ProductPlant" +
                  $"?$filter={filter}" +
                  $"&$select=Product,Plant" +
                  $"&$format=json";

        Console.WriteLine($"[DEBUG] ProductPlant URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return "{}";

        return result;
    }
    private static readonly Dictionary<string, string> _storageClassMap = new()
    {
        ["001"] = "3A : ของเหลวไวไฟ",
        ["002"] = "3B : ของเหลวไวไฟ",
        ["003"] = "4.1A : ของแข็งไวไฟ",
        ["004"] = "4.1B : ของแข็งไวไฟ",
        ["005"] = "4.2 : สารที่มีความเสี่ยงต่อการลุกติดไฟได้เอง",
        ["006"] = "4.3 : สารที่ให้กาซไวไฟเมื่อสัมผัสกับน้ำ",
        ["007"] = "5.1A : สารออกซิไดซ์",
        ["008"] = "5.1B : สารออกซิไดซ์",
        ["009"] = "5.1C : สารออกซิไดซ์",
        ["010"] = "5.2 : สารเปอร์ออกไซด์อินทรีย์",
        ["011"] = "6.1A : สารติดไฟที่มีคุณสมบัติความเป็นพิษ",
        ["012"] = "6.1B : สารไม่ติดไฟที่มีคุณสมบัติความเป็นพิษ",
        ["013"] = "8A : สารติดไฟที่มีคุณสมบัติกัดกร่อน",
        ["014"] = "8B : สารไม่ติดไฟที่มีคุณสมบัติกัดกร่อน",
        ["015"] = "10 : ของเหลวติดไฟที่ไม่อยู่ในประเภท 3A หรือ 3B",
        ["016"] = "11 : ของแข็งติดไฟ",
        ["017"] = "12 : ของเหลวไม่ติดไฟ",
        ["018"] = "13 : ของแข็งไม่ติดไฟ",
        ["999"] = "Other"
    };

    public string? GetStorageClassDescription(string? code)
        => code != null && _storageClassMap.TryGetValue(code, out var desc) ? desc : null;

    public async Task<string> GetCustomersByCodesAsync(List<string> customerCodes)
    {
        var baseUrl = _configuration["SapConfig:BusinessPartner:BaseUrl"];
        var authHeader = _configuration["SapConfig:BusinessPartner:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // สร้าง filter: Customer eq '1000031' or Customer eq '1000247' or ...
        var filter = string.Join(" or ", customerCodes.Select(c => $"Customer eq '{c.Trim()}'"));

        var url = $"{baseUrl!.TrimEnd('/')}/A_Customer" +
                  $"?$top={customerCodes.Count + 10}&$format=json" +
                  $"&$select=Customer,CustomerFullName" +
                  $"&$filter={filter}";

        Console.WriteLine($"[DEBUG] CustomersByCodes URL: {url}");

        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP Error: {result}");

        return result;
    }

    public async Task<Dictionary<string, SapAddressResult>> GetShipToAddressesBatchAsync(
    List<string> customerIds)
    {
        var result = new Dictionary<string, SapAddressResult>(
            StringComparer.OrdinalIgnoreCase);

        if (!customerIds.Any()) return result;

        var baseUrl = _configuration["SapConfig:BusinessPartner:BaseUrl"];
        var authHeader = _configuration["SapConfig:BusinessPartner:AuthHeader"];

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        try
        {
            var bpFilter = string.Join(" or ",
                customerIds.Select(c => $"BusinessPartner eq '{c.Trim()}'"));

            var url = $"{baseUrl!.TrimEnd('/')}/A_BusinessPartnerAddress" +
          $"?$filter={bpFilter}" +
          $"&$select=BusinessPartner,HouseNumber,HouseNumberSupplementText," +
          $"StreetName,StreetPrefixName,StreetSuffixName," +  // ✅ เพิ่ม StreetSuffixName
          $"AdditionalStreetPrefixName,AdditionalStreetSuffixName," +
          $"District,CityName,PostalCode,Region,Country" +
          $"&$format=json";

            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return result;

            var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var item in json.RootElement
                .GetProperty("d").GetProperty("results").EnumerateArray())
            {
                var bp = item.TryGetProperty("BusinessPartner", out var b)
                    ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(bp)) continue;

                if (!result.ContainsKey(bp!))
                    result[bp!] = new SapAddressResult
                    {
                        HouseNumber = item.TryGetProperty("HouseNumber", out var hn)
                                        ? hn.GetString() : null,
                        // ✅ เพิ่ม — บ้านเลขที่บางตัวอยู่ที่นี่
                        HouseNumberSupplement = item.TryGetProperty("HouseNumberSupplementText", out var hns)
                                        ? hns.GetString() : null,
                        StreetName = item.TryGetProperty("StreetName", out var st)
                                        ? st.GetString() : null,
                        StreetPrefix = item.TryGetProperty("StreetPrefixName", out var sp)
                                        ? sp.GetString() : null,
                        StreetSuffix = item.TryGetProperty("StreetSuffixName", out var ss)
                                        ? ss.GetString() : null,  // ✅ "1070 SOI SUANPLU"
                        AdditionalStreetPrefix = item.TryGetProperty("AdditionalStreetPrefixName", out var asp)
                                        ? asp.GetString() : null,
                        AdditionalStreetSuffix = item.TryGetProperty("AdditionalStreetSuffixName", out var ass)
                                        ? ass.GetString() : null,
                        District = item.TryGetProperty("District", out var di)
                                        ? di.GetString() : null,  // ✅ "TUNGMAHAMEK"
                        CityName = item.TryGetProperty("CityName", out var ci)
                                        ? ci.GetString() : null,
                        PostalCode = item.TryGetProperty("PostalCode", out var po)
                                        ? po.GetString() : null,
                        Region = item.TryGetProperty("Region", out var re)
                                        ? re.GetString() : null,
                        Country = item.TryGetProperty("Country", out var co)
                                        ? co.GetString() : null,
                    };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetShipToAddressesBatchAsync: {ex.Message}");
        }

        return result;
    }
    // ───── MB51: Material Document (API_MATERIAL_DOCUMENT_SRV, OData V2) ─────
    // ⚠️ PostingDate/DocumentDate/CreatedByUser อยู่บน header เท่านั้น จึงต้อง expand เสมอ
    public async Task<List<SapMaterialDocumentRow>> GetMaterialDocumentRowsAsync(
        string? material = null,
        string? plant = null,
        string? storageLocation = null,
        string? batch = null,
        DateTime? postingDateFrom = null,
        DateTime? postingDateTo = null,
        string? movementType = null,
        string? materialDocument = null,
        string? materialDocumentYear = null,
        int top = 2000)
    {
        var baseUrl = _configuration["SapConfig:MaterialDocument:BaseUrl"];
        var authHeader = _configuration["SapConfig:MaterialDocument:AuthHeader"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(authHeader))
            throw new Exception("Missing config: SapConfig:MaterialDocument (BaseUrl/AuthHeader)");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var rows = new List<SapMaterialDocumentRow>();

        // field ที่มีจริงบน item (ยืนยันจาก $metadata แล้ว)
        const string itemSelect =
            "MaterialDocument,MaterialDocumentYear,MaterialDocumentItem," +
            "Material,Plant,StorageLocation,Batch,GoodsMovementType,GoodsMovementRefDocType," +
            "GoodsMovementReasonCode,InventoryStockType,DebitCreditCode," +
            "QuantityInEntryUnit,EntryUnit,QuantityInBaseUnit,MaterialBaseUnit," +
            "GdsMvtExtAmtInCoCodeCrcy,CompanyCodeCurrency," +
            "PurchaseOrder,PurchaseOrderItem,Delivery,DeliveryItem," +
            "SalesOrder,SalesOrderItem,Customer,Supplier,Reservation," +
            "MaterialDocumentItemText,ShelfLifeExpirationDate,ManufactureDate," +
            "GoodsMovementIsCancelled,ReversedMaterialDocument,ReversedMaterialDocumentYear," +
            "IssuingOrReceivingPlant,IssuingOrReceivingStorageLoc,FiscalYear,FiscalYearPeriod";

        var useItemEntry = !string.IsNullOrWhiteSpace(material)
                        || !string.IsNullOrWhiteSpace(materialDocument);

        string url;

        if (useItemEntry)
        {
            // ── ทาง A: เข้าทาง item (มี Material หรือเลขเอกสาร) ──────────────
            var f = new List<string>();
            if (!string.IsNullOrWhiteSpace(materialDocument))
                f.Add($"MaterialDocument eq '{materialDocument.Trim()}'");
            if (!string.IsNullOrWhiteSpace(materialDocumentYear))
                f.Add($"MaterialDocumentYear eq '{materialDocumentYear.Trim()}'");
            if (!string.IsNullOrWhiteSpace(material))
                f.Add($"Material eq '{material.Trim()}'");
            if (!string.IsNullOrWhiteSpace(plant))
                f.Add($"Plant eq '{plant.Trim()}'");
            if (!string.IsNullOrWhiteSpace(storageLocation))
                f.Add($"StorageLocation eq '{storageLocation.Trim()}'");
            if (!string.IsNullOrWhiteSpace(batch))
                f.Add($"Batch eq '{batch.Trim()}'");
            if (!string.IsNullOrWhiteSpace(movementType))
                f.Add($"GoodsMovementType eq '{movementType.Trim()}'");

            url = $"{baseUrl.TrimEnd('/')}/A_MaterialDocumentItem" +
                  $"?$top={top}&$format=json" +
                  $"&$expand=to_MaterialDocumentHeader" +
                  $"&$select={itemSelect}," +
                  $"to_MaterialDocumentHeader/PostingDate," +
                  $"to_MaterialDocumentHeader/DocumentDate," +
                  $"to_MaterialDocumentHeader/CreatedByUser," +
                  $"to_MaterialDocumentHeader/ReferenceDocument" +
                  $"&$filter={string.Join(" and ", f)}";
        }
        else
        {
            // ── ทาง B: เข้าทาง header (มีแต่ช่วงวันที่) ───────────────────────
            var f = new List<string>();
            if (postingDateFrom.HasValue)
                f.Add($"PostingDate ge datetime'{postingDateFrom.Value:yyyy-MM-dd}T00:00:00'");
            if (postingDateTo.HasValue)
                f.Add($"PostingDate le datetime'{postingDateTo.Value:yyyy-MM-dd}T00:00:00'");

            if (!f.Any()) return rows;

            url = $"{baseUrl.TrimEnd('/')}/A_MaterialDocumentHeader" +
                  $"?$top={top}&$format=json" +
                  $"&$expand=to_MaterialDocumentItem" +
                  $"&$filter={string.Join(" and ", f)}";
        }

        Console.WriteLine($"[DEBUG] MaterialDocument URL: {url}");

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"SAP MaterialDocument Error: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("d", out var d) ||
            !d.TryGetProperty("results", out var results))
            return rows;

        if (useItemEntry)
        {
            foreach (var item in results.EnumerateArray())
            {
                JsonElement? hdr = item.TryGetProperty("to_MaterialDocumentHeader", out var h) &&
                                   h.ValueKind == JsonValueKind.Object &&
                                   h.TryGetProperty("PostingDate", out _)
                                   ? h : (JsonElement?)null;
                rows.Add(MapRow(item, hdr));
            }
        }
        else
        {
            foreach (var header in results.EnumerateArray())
            {
                if (!header.TryGetProperty("to_MaterialDocumentItem", out var nav) ||
                    !nav.TryGetProperty("results", out var itemArr)) continue;

                foreach (var item in itemArr.EnumerateArray())
                    rows.Add(MapRow(item, header));
            }
        }

        // กรองต่อในฝั่ง C# สำหรับเงื่อนไขที่ยิงไป SAP ไม่ได้ในทางนั้น ๆ
        if (useItemEntry)
        {
            if (postingDateFrom.HasValue)
                rows = rows.Where(r => r.PostingDate >= postingDateFrom.Value.Date).ToList();
            if (postingDateTo.HasValue)
                rows = rows.Where(r => r.PostingDate < postingDateTo.Value.Date.AddDays(1)).ToList();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(plant))
                rows = rows.Where(r => r.Plant == plant.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(storageLocation))
                rows = rows.Where(r => r.StorageLocation == storageLocation.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(batch))
                rows = rows.Where(r => r.Batch?.Trim() == batch.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(movementType))
                rows = rows.Where(r => r.GoodsMovementType == movementType.Trim()).ToList();
        }

        return rows;
    }
    // ───── MB51: Movement Type description ──────────────────────────────
    private static readonly Dictionary<string, string> _movementTypeMap = new()
    {
        ["101"] = "รับเข้าจาก PO (GR)",
        ["102"] = "ยกเลิกรับเข้าจาก PO",
        ["122"] = "ส่งคืนผู้ขาย",
        ["201"] = "เบิกเข้า Cost Center",
        ["202"] = "ยกเลิกเบิก Cost Center",
        ["261"] = "เบิกเข้า Order",
        ["262"] = "ยกเลิกเบิก Order",
        ["301"] = "โอนย้ายข้าม Plant",
        ["311"] = "โอนย้ายข้าม Storage Location",
        ["321"] = "QI → Unrestricted",
        ["343"] = "Blocked → Unrestricted",
        ["501"] = "รับเข้าโดยไม่มี PO",
        ["561"] = "ตั้งยอดยกมา",
        ["601"] = "จ่ายออกตาม Delivery",
        ["602"] = "ยกเลิกจ่ายออกตาม Delivery",
        ["641"] = "จ่ายออกตาม STO",
        ["643"] = "จ่ายออก STO ข้ามบริษัท",
        ["701"] = "ปรับยอดเพิ่ม (ตรวจนับ)",
        ["702"] = "ปรับยอดลด (ตรวจนับ)",
    };

    public string? GetMovementTypeDescription(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
         : _movementTypeMap.TryGetValue(code.Trim(), out var desc) ? desc : code.Trim();
    private static SapMaterialDocumentRow MapRow(JsonElement item, JsonElement? header)
    {
        static string? S(JsonElement el, string key) =>
            el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;

        static decimal? Dec(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var p)) return null;
            var raw = p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText();
            return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        static DateTime? Dt(JsonElement el, string key)
        {
            var raw = S(el, key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(raw, @"-?\d+");
            return m.Success && long.TryParse(m.Value, out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
        }

        return new SapMaterialDocumentRow
        {
            MaterialDocument = S(item, "MaterialDocument"),
            MaterialDocumentYear = S(item, "MaterialDocumentYear"),
            MaterialDocumentItem = S(item, "MaterialDocumentItem"),

            PostingDate = header.HasValue ? Dt(header.Value, "PostingDate") : null,
            DocumentDate = header.HasValue ? Dt(header.Value, "DocumentDate") : null,
            CreatedByUser = header.HasValue ? S(header.Value, "CreatedByUser") : null,
            ReferenceDocument = header.HasValue ? S(header.Value, "ReferenceDocument") : null,

            Material = S(item, "Material"),
            Plant = S(item, "Plant"),
            StorageLocation = S(item, "StorageLocation"),
            Batch = S(item, "Batch")?.Trim(),

            GoodsMovementType = S(item, "GoodsMovementType"),
            GoodsMovementRefDocType = S(item, "GoodsMovementRefDocType"),
            GoodsMovementReasonCode = S(item, "GoodsMovementReasonCode"),
            InventoryStockType = S(item, "InventoryStockType"),
            DebitCreditCode = S(item, "DebitCreditCode"),

            QuantityInEntryUnit = Dec(item, "QuantityInEntryUnit"),
            EntryUnit = S(item, "EntryUnit"),
            QuantityInBaseUnit = Dec(item, "QuantityInBaseUnit"),
            MaterialBaseUnit = S(item, "MaterialBaseUnit"),

            ExternalAmount = Dec(item, "GdsMvtExtAmtInCoCodeCrcy"),
            CompanyCodeCurrency = S(item, "CompanyCodeCurrency"),

            PurchaseOrder = S(item, "PurchaseOrder"),
            PurchaseOrderItem = S(item, "PurchaseOrderItem"),
            Delivery = S(item, "Delivery"),
            DeliveryItem = S(item, "DeliveryItem"),
            SalesOrder = S(item, "SalesOrder"),
            SalesOrderItem = S(item, "SalesOrderItem"),
            Customer = S(item, "Customer"),
            Supplier = S(item, "Supplier"),
            Reservation = S(item, "Reservation"),

            ItemText = S(item, "MaterialDocumentItemText"),
            ShelfLifeExpirationDate = Dt(item, "ShelfLifeExpirationDate"),
            ManufactureDate = Dt(item, "ManufactureDate"),

            IsCancelled = item.TryGetProperty("GoodsMovementIsCancelled", out var gc) &&
                          gc.ValueKind == JsonValueKind.True,
            ReversedMaterialDocument = S(item, "ReversedMaterialDocument"),

            IssuingOrReceivingPlant = S(item, "IssuingOrReceivingPlant"),
            IssuingOrReceivingStorageLoc = S(item, "IssuingOrReceivingStorageLoc"),
        };
    }
    // ✅ วัน go-live ของ S/4HANA — จุดตัดระหว่าง DWH (billing) กับ MB51
    //    ปัดเป็นวันที่ 1 ของเดือนเสมอ เพื่อไม่ให้มีรอยต่อกลางเดือน
    public DateTime MaterialDocumentGoLiveDate
    {
        get
        {
            var raw = _configuration["SapConfig:MaterialDocument:GoLiveDate"];
            return DateTime.TryParse(raw, out var d)
                ? new DateTime(d.Year, d.Month, 1)
                : new DateTime(2025, 7, 1);
        }
    }
    public class SapMaterialDocumentRow
    {
        public string? MaterialDocument { get; set; }
        public string? MaterialDocumentYear { get; set; }
        public string? MaterialDocumentItem { get; set; }
        public DateTime? PostingDate { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? CreatedByUser { get; set; }
        public string? ReferenceDocument { get; set; }
        public string? Material { get; set; }
        public string? Plant { get; set; }
        public string? StorageLocation { get; set; }
        public string? Batch { get; set; }
        public string? GoodsMovementType { get; set; }
        public string? GoodsMovementRefDocType { get; set; }
        public string? GoodsMovementReasonCode { get; set; }
        public string? InventoryStockType { get; set; }
        public string? DebitCreditCode { get; set; }
        public decimal? QuantityInEntryUnit { get; set; }
        public string? EntryUnit { get; set; }
        public decimal? QuantityInBaseUnit { get; set; }
        public string? MaterialBaseUnit { get; set; }
        public decimal? ExternalAmount { get; set; }
        public string? CompanyCodeCurrency { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? PurchaseOrderItem { get; set; }
        public string? Delivery { get; set; }
        public string? DeliveryItem { get; set; }
        public string? SalesOrder { get; set; }
        public string? SalesOrderItem { get; set; }
        public string? Customer { get; set; }
        public string? Supplier { get; set; }
        public string? Reservation { get; set; }
        public string? ItemText { get; set; }
        public DateTime? ShelfLifeExpirationDate { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public bool IsCancelled { get; set; }
        public string? ReversedMaterialDocument { get; set; }
        public string? IssuingOrReceivingPlant { get; set; }
        public string? IssuingOrReceivingStorageLoc { get; set; }
    }
    public class SapAddressResult
    {
        public string? HouseNumber { get; set; }
        public string? HouseNumberSupplement { get; set; }
        public string? StreetPrefix { get; set; }
        public string? StreetName { get; set; }
        public string? StreetSuffix { get; set; }  // ✅ "1070 SOI SUANPLU"
        public string? AdditionalStreetPrefix { get; set; }
        public string? AdditionalStreetSuffix { get; set; }
        public string? District { get; set; }  // ✅ "TUNGMAHAMEK"
        public string? CityName { get; set; }
        public string? PostalCode { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
    }
}