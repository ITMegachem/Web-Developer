using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.WebFront.Auth;

namespace Mgt.Lit.WebFront.Services.DashBoard
{
    public class DashSalesOverviewApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;

        public DashSalesOverviewApiService(HttpClient http, AuthState auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<SalesOverviewDto?> GetOverviewAsync(SalesOverviewFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.Year.HasValue) qs.Add($"Year={filter.Year}");
            if (!string.IsNullOrWhiteSpace(filter.AffiliateCustomerName)) qs.Add($"AffiliateCustomerName={Uri.EscapeDataString(filter.AffiliateCustomerName)}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");
            if (!string.IsNullOrWhiteSpace(filter.MaterialGroupName)) qs.Add($"MaterialGroupName={Uri.EscapeDataString(filter.MaterialGroupName)}");
            if (!string.IsNullOrWhiteSpace(filter.IndustryName)) qs.Add($"IndustryName={Uri.EscapeDataString(filter.IndustryName)}");
            if (filter.MonthId.HasValue) qs.Add($"MonthId={filter.MonthId}");

            var url = "api/dashboard/salesoverview" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            // ★ แนบ JWT ต่อ request — กัน 401
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var token = _auth.Token;   // ★★ ปรับให้ตรงกับ property/method ที่เก็บ JWT จริงใน AuthState
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<SalesOverviewDto>(cancellationToken: ct);
        }
    }
}
