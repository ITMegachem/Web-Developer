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
    public class DashSalesProductivityApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;

        public DashSalesProductivityApiService(HttpClient http, AuthState auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<SalesProductivityDto?> GetAsync(SalesProductivityFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.DateFrom.HasValue) qs.Add($"DateFrom={filter.DateFrom:yyyy-MM-dd}");
            if (filter.DateTo.HasValue) qs.Add($"DateTo={filter.DateTo:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");
            if (!string.IsNullOrWhiteSpace(filter.Stage)) qs.Add($"Stage={Uri.EscapeDataString(filter.Stage)}");
            if (!string.IsNullOrWhiteSpace(filter.Product)) qs.Add($"Product={Uri.EscapeDataString(filter.Product)}");
            if (!string.IsNullOrWhiteSpace(filter.CustomerName)) qs.Add($"CustomerName={Uri.EscapeDataString(filter.CustomerName)}");

            var url = "api/dashboard/salesproductivity" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            // ★ แนบ JWT ต่อ request — กัน 401
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var token = _auth.Token;
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<SalesProductivityDto>(cancellationToken: ct);
        }
    }
}
