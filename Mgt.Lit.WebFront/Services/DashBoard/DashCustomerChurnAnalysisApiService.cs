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
    public class DashCustomerChurnAnalysisApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;

        public DashCustomerChurnAnalysisApiService(HttpClient http, AuthState auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<CustomerChurnAnalysisDto?> GetAsync(CustomerChurnAnalysisFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.Year.HasValue) qs.Add($"Year={filter.Year}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");
            if (!string.IsNullOrWhiteSpace(filter.CustomerGroup)) qs.Add($"CustomerGroup={Uri.EscapeDataString(filter.CustomerGroup)}");
            if (!string.IsNullOrWhiteSpace(filter.IndustryName)) qs.Add($"IndustryName={Uri.EscapeDataString(filter.IndustryName)}");

            var url = "api/dashboard/customerchurnanalysis" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            // ★ แนบ JWT ต่อ request — กัน 401
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var token = _auth.Token;
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<CustomerChurnAnalysisDto>(cancellationToken: ct);
        }
    }
}
