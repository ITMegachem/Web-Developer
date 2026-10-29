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
    public class DashOpportunityWinRateApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;

        public DashOpportunityWinRateApiService(HttpClient http, AuthState auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<OpportunityWinRateDto?> GetAsync(OpportunityWinRateFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.DateFrom.HasValue) qs.Add($"DateFrom={filter.DateFrom:yyyy-MM-dd}");
            if (filter.DateTo.HasValue) qs.Add($"DateTo={filter.DateTo:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");
            if (!string.IsNullOrWhiteSpace(filter.Product)) qs.Add($"Product={Uri.EscapeDataString(filter.Product)}");
            if (!string.IsNullOrWhiteSpace(filter.IndustryName)) qs.Add($"IndustryName={Uri.EscapeDataString(filter.IndustryName)}");

            var url = "api/dashboard/opportunitywinrate" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            // Valid token (refreshed if expired) + one retry after refresh on 401 — see AuthState.SendAsync
            using var resp = await _auth.SendAsync(_http, token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            }, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null; // session ended → popup via AuthState.SessionEnded
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<OpportunityWinRateDto>(cancellationToken: ct);
        }
    }
}
