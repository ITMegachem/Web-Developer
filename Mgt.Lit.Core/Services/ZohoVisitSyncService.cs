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
    // ดึงข้อมูลการเข้าเยี่ยมลูกค้าจาก Zoho CRM (crm/v8) module "Visit_Reports" (+ subform "Visit_Items")
    // มาซิงก์เข้าตาราง MGT_VisitReport / MGT_VisitItem ที่ใช้เป็นฐานข้อมูลของ Visit Daily Report
    //
    // ★★★ ยืนยันกับข้อมูลจริงแล้ว (ผ่าน settings/modules, settings/fields, และ sample record) ★★★
    // - Module จริงชื่อ "Visit_Reports" (custom module) — มี module เก่าชื่อ "Visits" ที่ถูกซ่อนไว้ (user_hidden) ไม่ใช้แล้ว
    // - SalesGroup(BU) ไม่ได้อยู่บน Visit_Reports โดยตรง ดึงผ่าน Account_Name.Sales_Group_BU (custom field บน Account
    //   เดียวกับที่ใช้ใน ZohoDealSyncService) ผ่าน COQL dot-notation
    // - Visit_Items เป็น subform — Zoho ไม่ส่งมาให้ใน list/COQL ต้องยิงทีละ Visit Report (GET /Visit_Reports/{id}?fields=Visit_Items)
    // - หลาย field ใน Visit_Items (Price, Consumption, Potential) เป็น free-text ที่ Sales กรอกเอง ไม่ใช่ตัวเลข/boolean มาตรฐาน
    //   จึงเก็บเป็น string ทั้งหมด ไม่แปลงเป็น decimal/bool
    // - ไม่มี field สถานะ "Completed" ตรงๆ บน Visit_Reports (Record_Status__s คือ Available/Draft/Trash คนละความหมาย)
    //   ฝั่ง reporting service จะ derive สถานะจาก EndDateTime มีค่าหรือไม่แทน
    public class ZohoVisitSyncService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;
        private readonly AppDbContext _db;
        private readonly ZohoAuthTokenProvider _tokenProvider;

        public ZohoVisitSyncService(IConfiguration config, HttpClient http, AppDbContext db, ZohoAuthTokenProvider tokenProvider)
        {
            _config = config;
            _http = http;
            _db = db;
            _tokenProvider = tokenProvider;
        }

        public async Task<int> SyncVisitReportsAsync(CancellationToken ct = default)
        {
            var (accessToken, apiDomain) = await _tokenProvider.GetAccessTokenAsync(ct);

            var salesGroupExpr = _config["ZohoConfig:FieldMap:SalesGroupField"] ?? "Account_Name.Sales_Group_BU";
            var selectFields = new List<string>
            {
                "id", "Name", "Owner", "Date", "Account_Name", salesGroupExpr,
                "Start_Date_Time", "End_Date_Time", "Description_Remark", "Created_Time", "Modified_Time"
            };

            var syncedCount = 0;
            var seenVisitIds = new List<string>();
            var offset = 0;
            const int pageSize = 200;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var query = $"select {string.Join(",", selectFields.Distinct())} from Visit_Reports where id is not null limit {pageSize} offset {offset}";
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
                    var visitId = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(visitId)) continue;
                    seenVisitIds.Add(visitId);

                    var visit = await _db.MGT_VisitReport.FindAsync(new object?[] { visitId }, ct);
                    if (visit is null)
                    {
                        visit = new MGT_VisitReport { VisitReportId = visitId };
                        _db.MGT_VisitReport.Add(visit);
                    }

                    visit.VisitNumber = GetString(item, "Name");
                    visit.SalesEmployeeBP = GetLookupName(item, "Owner");
                    visit.VisitDate = GetDateTime(item, "Date");
                    visit.CustomerCode = GetLookupId(item, "Account_Name");
                    visit.CustomerName = GetLookupName(item, "Account_Name");
                    visit.SalesGroup = GetString(item, salesGroupExpr);
                    visit.StartDateTime = GetDateTime(item, "Start_Date_Time");
                    visit.EndDateTime = GetDateTime(item, "End_Date_Time");
                    visit.DescriptionRemark = GetString(item, "Description_Remark");
                    visit.CreatedTime = GetDateTime(item, "Created_Time");
                    visit.ModifiedTime = GetDateTime(item, "Modified_Time");

                    syncedCount++;
                }

                await _db.SaveChangesAsync(ct);

                var moreRecords = root.TryGetProperty("info", out var info)
                    && info.TryGetProperty("more_records", out var mr)
                    && mr.ValueKind == JsonValueKind.True;

                if (!moreRecords) break;
                offset += pageSize;
            }

            await SyncVisitItemsAsync(apiDomain, accessToken, seenVisitIds, ct);

            return syncedCount;
        }

        // Visit_Items (บรรทัดสินค้า/คู่แข่งที่คุยระหว่างเข้าเยี่ยม) เป็น subform — ต้องยิงทีละ Visit Report
        private async Task SyncVisitItemsAsync(string apiDomain, string accessToken, List<string> visitIds, CancellationToken ct)
        {
            foreach (var visitId in visitIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var url = $"{apiDomain.TrimEnd('/')}/crm/v8/Visit_Reports/{visitId}?fields=Visit_Items";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("Authorization", $"Zoho-oauthtoken {accessToken}");

                    using var resp = await _http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode) continue;

                    var body = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(body);
                    if (!doc.RootElement.TryGetProperty("data", out var records) || records.GetArrayLength() == 0) continue;

                    var record = records[0];
                    var existingItems = await _db.MGT_VisitItem.Where(p => p.VisitReportId == visitId).ToListAsync(ct);
                    _db.MGT_VisitItem.RemoveRange(existingItems);

                    if (record.TryGetProperty("Visit_Items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            _db.MGT_VisitItem.Add(new MGT_VisitItem
                            {
                                VisitReportId = visitId,
                                MaterialName = GetLookupName(item, "Material_Name") ?? GetString(item, "Material_Code"),
                                MaterialCode = GetString(item, "Material_Code"),
                                MaterialGroup = GetString(item, "Material_Group"),
                                CompetitorName = GetString(item, "Competitor_Name"),
                                CompetitorSupplier = GetString(item, "Competitor_Supplier"),
                                ContactPerson = GetString(item, "Contact_Person"),
                                Position = GetString(item, "Position"),
                                Price = GetString(item, "Price"),
                                Consumption = GetString(item, "Consumption"),
                                MakerOrigin = GetString(item, "Maker_Origin"),
                                Application = GetString(item, "Application"),
                                Potential = GetString(item, "Potential"),
                                StatusNote = GetString(item, "Status_Note")
                            });
                        }
                    }
                }
                catch
                {
                    // best-effort — ปัญหาของ visit เดียวไม่ควรทำให้ sync ทั้งชุดล้ม
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        private static string? GetString(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static DateTime? GetDateTime(JsonElement obj, string field)
        {
            if (!obj.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) return null;
            return DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        }

        private static string? GetLookupName(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var n)
                ? n.GetString() : null;

        private static string? GetLookupId(JsonElement obj, string field) =>
            obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var i)
                ? i.GetString() : null;
    }
}
