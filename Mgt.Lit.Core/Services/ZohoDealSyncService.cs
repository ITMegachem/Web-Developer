using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.Core.Services
{
    // ดึง Opportunity/Deal จาก Zoho CRM (crm/v8) มาซิงก์เข้าตาราง MGT_Deal / MGT_DealProduct
    // ที่ใช้เป็นฐานข้อมูลของ Opportunity Win Rate Report
    //
    // ★★★ ตั้งค่าก่อนใช้งาน ★★★ — appsettings.json (หรือ user-secrets) -> "ZohoConfig"
    // ClientId/ClientSecret/RefreshToken ได้จากการสร้าง Server-based Application ใน Zoho API Console
    // (https://api-console.zoho.com) — ควรเก็บผ่าน `dotnet user-secrets` ไม่ใช่ commit ลง appsettings.json ตรงๆ
    // การขอ/cache access token ทำโดย ZohoAuthTokenProvider (แยกออกมาต่างหาก เพราะต้อง register เป็น Singleton
    // เพื่อแชร์ token cache ข้าม request ได้ ในขณะที่ service นี้เป็น Scoped เพราะพึ่ง AppDbContext)
    //
    // ★★★ ยืนยันกับข้อมูลจริงแล้ว (ผ่าน settings/fields + COQL) ★★★
    // - Stage เป็นชื่อที่แต่ละองค์กรตั้งเอง (เช่น "Closed Won - Exact Match") จึงต้องจัดกลุ่ม Won/Lost/Open
    //   ผ่าน "forecast_category" ของแต่ละ Stage picklist value (Pipeline/Closed/Omitted) ไม่ใช่เทียบชื่อ Stage ตรงๆ
    // - Industry และ SalesGroup(BU) เป็น custom field ที่อยู่บน Account (ลูกค้า) ไม่ใช่บน Deal โดยตรง
    //   ดึงผ่าน COQL dot-notation "Account_Name.Industry" / "Account_Name.Sales_Group_BU"
    // - Lost Reason คือ field มาตรฐาน "Reason_For_Loss__s" ("Reason For Loss")
    // - Actual Closing Date คือ custom field "Actual_Closing_Date" — องค์กรนี้กรอกเฉพาะตอน Won เท่านั้น
    //   ตอน Lost/Cancelled จะว่าง จึง fallback ไป Closing_Date เสมอ (ดู EffectiveClosedDate ฝั่ง reporting service)
    // - Amount (field มาตรฐาน) ไม่ได้ใช้งานจริง องค์กรนี้คิดยอด Deal จาก formula field "Grand_Total"
    //   (ผลรวมจาก Deal_Items subform) แทน — ใช้ ZohoConfig:FieldMap:DealAmountField ชี้ไปที่ field นี้
    // - Product เป็น subform "Deal_Items" (บรรทัดสินค้า) — Zoho ไม่ส่ง subform มาใน list API ต้องยิงทีละ Deal
    //   (GET /Deals/{id}?fields=Deal_Items) โดยชื่อสินค้าอยู่ที่ Deal_Items[].Material_Name.name
    // ค่า default ด้านล่างตรงกับที่ตรวจสอบจริงกับ Zoho องค์กรนี้แล้ว — ถ้าย้ายไปใช้กับ Zoho องค์กรอื่นต้องเช็คใหม่
    public class ZohoDealSyncService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;
        private readonly AppDbContext _db;
        private readonly ZohoAuthTokenProvider _tokenProvider;

        public ZohoDealSyncService(IConfiguration config, HttpClient http, AppDbContext db, ZohoAuthTokenProvider tokenProvider)
        {
            _config = config;
            _http = http;
            _db = db;
            _tokenProvider = tokenProvider;
        }

        public async Task<int> SyncDealsAsync(CancellationToken ct = default)
        {
            var (accessToken, apiDomain) = await _tokenProvider.GetAccessTokenAsync(ct);
            var stageForecastMap = await GetStageForecastMapAsync(apiDomain, accessToken, ct);

            var industryExpr = _config["ZohoConfig:FieldMap:IndustryField"] ?? "Account_Name.Industry";
            var salesGroupExpr = _config["ZohoConfig:FieldMap:SalesGroupField"] ?? "Account_Name.Sales_Group_BU";
            var lostReasonField = _config["ZohoConfig:FieldMap:LostReasonField"] ?? "Reason_For_Loss__s";
            var actualClosedField = _config["ZohoConfig:FieldMap:ActualClosedDateField"] ?? "Actual_Closing_Date";
            // ★ หลายองค์กรไม่ได้กรอก field "Amount" มาตรฐาน แต่ใช้ formula field รวมยอดจาก Deal_Items แทน (เช่น "Grand_Total")
            var dealAmountField = _config["ZohoConfig:FieldMap:DealAmountField"] ?? "Amount";
            // "Forecast_Status" (picklist: -None-/Yes/No) — Sales ทำเครื่องหมายเองว่า deal นี้นับเข้า forecast รอบนี้หรือไม่
            var forecastStatusField = _config["ZohoConfig:FieldMap:ForecastStatusField"] ?? "Forecast_Status";

            var selectFields = new List<string> { "id", "Deal_Name", "Created_Time", "Closing_Date", "Stage", dealAmountField, "Owner", "Account_Name", lostReasonField, industryExpr, salesGroupExpr, forecastStatusField };
            if (!string.IsNullOrWhiteSpace(actualClosedField)) selectFields.Add(actualClosedField);

            var syncedCount = 0;
            var seenDealIds = new List<string>();
            var offset = 0;
            const int pageSize = 200; // ค่าสูงสุดต่อ request ของ Zoho COQL

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var query = $"select {string.Join(",", selectFields.Distinct())} from Deals where id is not null limit {pageSize} offset {offset}";
                using var reqContent = new StringContent(JsonSerializer.Serialize(new { select_query = query }), Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{apiDomain.TrimEnd('/')}/crm/v8/coql") { Content = reqContent };
                req.Headers.Add("Authorization", $"Zoho-oauthtoken {accessToken}");

                using var resp = await _http.SendAsync(req, ct);
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) break;

                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Zoho COQL error (HTTP {(int)resp.StatusCode}): {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var dataArray)) break;

                foreach (var item in dataArray.EnumerateArray())
                {
                    var dealId = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(dealId)) continue;
                    seenDealIds.Add(dealId);

                    var deal = await _db.MGT_Deal.FindAsync(new object?[] { dealId }, ct);
                    if (deal is null)
                    {
                        deal = new MGT_Deal { DealId = dealId };
                        _db.MGT_Deal.Add(deal);
                    }

                    var stage = GetString(item, "Stage");

                    deal.OpportunityName = GetString(item, "Deal_Name");
                    deal.CreatedDate = GetDateTime(item, "Created_Time");
                    deal.ClosingDate = GetDateTime(item, "Closing_Date");
                    deal.ActualClosedDate = !string.IsNullOrWhiteSpace(actualClosedField) ? GetDateTime(item, actualClosedField) : null;
                    deal.Stage = stage;
                    deal.ForecastCategory = stage != null && stageForecastMap.TryGetValue(stage, out var fc) ? fc : null;
                    deal.SalesGroup = GetString(item, salesGroupExpr);
                    deal.SalesEmployeeBP = GetLookupName(item, "Owner");
                    deal.CustomerCode = GetLookupId(item, "Account_Name");
                    deal.CustomerName = GetLookupName(item, "Account_Name");
                    deal.IndustryName = GetString(item, industryExpr);
                    deal.DealAmount = GetDecimal(item, dealAmountField);
                    deal.LostReason = GetString(item, lostReasonField);
                    deal.ForecastStatus = GetString(item, forecastStatusField);

                    syncedCount++;
                }

                await _db.SaveChangesAsync(ct);

                var moreRecords = root.TryGetProperty("info", out var info)
                    && info.TryGetProperty("more_records", out var mr)
                    && mr.ValueKind == JsonValueKind.True;

                if (!moreRecords) break;
                offset += pageSize;
            }

            if (!bool.TryParse(_config["ZohoConfig:SyncProducts"], out var syncProducts) || syncProducts)
                await SyncDealProductsAsync(apiDomain, accessToken, seenDealIds, ct);

            // ★ capture forecast snapshot ทุกครั้งที่ sync เสร็จ — สะสมประวัติ "Forecast ที่ล็อกไว้ก่อนรู้ Actual"
            // ไปเรื่อยๆ สำหรับ Sales Forecast Accuracy Report (วันนี้ยังไม่มีข้อมูลเก่า เริ่มนับจากนี้ไป)
            await CaptureForecastSnapshotAsync(ct);

            return syncedCount;
        }

        // บันทึก Deal ที่ยังเปิดอยู่ (ForecastCategory = Pipeline) ทั้งหมด ณ ตอนนี้ เป็น 1 snapshot ของวันนี้
        // เรียกซ้ำในวันเดียวกันได้ — จะลบ snapshot เก่าของวันนี้ทิ้งก่อนแล้วบันทึกใหม่ (idempotent ต่อวัน)
        public async Task<int> CaptureForecastSnapshotAsync(CancellationToken ct = default)
        {
            var today = DateTime.Now.Date;

            var existingToday = _db.MGT_ForecastSnapshot.Where(s => s.SnapshotDate == today);
            _db.MGT_ForecastSnapshot.RemoveRange(existingToday);
            await _db.SaveChangesAsync(ct);

            var openDeals = await _db.MGT_Deal.AsNoTracking()
                .Where(d => d.ForecastCategory == "Pipeline" && d.ClosingDate.HasValue && d.DealAmount.HasValue)
                .ToListAsync(ct);

            foreach (var deal in openDeals)
            {
                _db.MGT_ForecastSnapshot.Add(new MGT_ForecastSnapshot
                {
                    SnapshotDate = today,
                    ForecastYear = deal.ClosingDate!.Value.Year,
                    ForecastMonth = deal.ClosingDate!.Value.Month,
                    DealId = deal.DealId,
                    SalesGroup = deal.SalesGroup,
                    SalesEmployeeBP = deal.SalesEmployeeBP,
                    CustomerName = deal.CustomerName,
                    IndustryName = deal.IndustryName,
                    ForecastAmount = deal.DealAmount!.Value
                });
            }

            await _db.SaveChangesAsync(ct);
            return openDeals.Count;
        }

        // ดึง forecast_category ของแต่ละ Stage picklist value (Pipeline/Closed/Omitted) ครั้งเดียวต่อรอบ sync
        private async Task<Dictionary<string, string>> GetStageForecastMapAsync(string apiDomain, string accessToken, CancellationToken ct)
        {
            var url = $"{apiDomain.TrimEnd('/')}/crm/v8/settings/fields?module=Deals";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", $"Zoho-oauthtoken {accessToken}");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Zoho fields metadata error (HTTP {(int)resp.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var map = new Dictionary<string, string>();
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return map;

            foreach (var field in fields.EnumerateArray())
            {
                if (GetString(field, "api_name") != "Stage") continue;
                if (!field.TryGetProperty("pick_list_values", out var values)) continue;

                foreach (var v in values.EnumerateArray())
                {
                    var label = GetString(v, "display_value");
                    var category = v.TryGetProperty("forecast_category", out var fcObj) && fcObj.ValueKind == JsonValueKind.Object
                        ? GetString(fcObj, "name") : null;
                    if (label != null && category != null) map[label] = category;
                }
            }
            return map;
        }

        // Deal_Items (บรรทัดสินค้า) เป็น subform — Zoho ไม่ส่งมาให้ใน list/COQL ต้องยิงทีละ Deal (GET /Deals/{id})
        private async Task SyncDealProductsAsync(string apiDomain, string accessToken, List<string> dealIds, CancellationToken ct)
        {
            foreach (var dealId in dealIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var url = $"{apiDomain.TrimEnd('/')}/crm/v8/Deals/{dealId}?fields=Deal_Items";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("Authorization", $"Zoho-oauthtoken {accessToken}");

                    using var resp = await _http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode) continue;

                    var body = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(body);
                    if (!doc.RootElement.TryGetProperty("data", out var records) || records.GetArrayLength() == 0) continue;

                    var record = records[0];
                    var existingLinks = await _db.MGT_DealProduct.Where(p => p.DealId == dealId).ToListAsync(ct);
                    _db.MGT_DealProduct.RemoveRange(existingLinks);

                    if (record.TryGetProperty("Deal_Items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var materialCode = GetString(item, "Material_Code");
                            var productName = GetLookupName(item, "Material_Name") ?? materialCode;
                            if (string.IsNullOrWhiteSpace(productName)) continue;
                            _db.MGT_DealProduct.Add(new MGT_DealProduct
                            {
                                DealId = dealId,
                                Product = productName,
                                MaterialGroup = GetString(item, "Material_Group"),
                                MaterialCode = materialCode,
                                Quantity = GetDecimal(item, "Quantity"),
                                RequiredDate = GetDateTime(item, "Required_Date"),
                                Unit = GetString(item, "Unit")
                            });
                        }
                    }
                }
                catch
                {
                    // best-effort — ปัญหาของ deal เดียวไม่ควรทำให้ sync ทั้งชุดล้ม
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        private static string? GetString(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static decimal? GetDecimal(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

        private static DateTime? GetDateTime(JsonElement obj, string field)
        {
            if (!obj.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) return null;
            return DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        }

        // Lookup field (เช่น Owner, Account_Name, Material_Name) ตอบกลับมาเป็น { "id": "...", "name": "..." }
        private static string? GetLookupName(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var n)
                ? n.GetString() : null;

        private static string? GetLookupId(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var i)
                ? i.GetString() : null;
    }
}
