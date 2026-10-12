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
    public class DashForecastMonthlyApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;

        public DashForecastMonthlyApiService(HttpClient http, AuthState auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<ForecastMonthlyDto?> GetAsync(ForecastMonthlyFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.FromMonth.HasValue) qs.Add($"FromMonth={filter.FromMonth:yyyy-MM-dd}");
            if (filter.ToMonth.HasValue) qs.Add($"ToMonth={filter.ToMonth:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(filter.CustomerName)) qs.Add($"CustomerName={Uri.EscapeDataString(filter.CustomerName)}");
            if (!string.IsNullOrWhiteSpace(filter.MaterialGroup)) qs.Add($"MaterialGroup={Uri.EscapeDataString(filter.MaterialGroup)}");
            if (!string.IsNullOrWhiteSpace(filter.MaterialCode)) qs.Add($"MaterialCode={Uri.EscapeDataString(filter.MaterialCode)}");
            if (!string.IsNullOrWhiteSpace(filter.JobDeal)) qs.Add($"JobDeal={Uri.EscapeDataString(filter.JobDeal)}");

            var url = "api/dashboard/forecastmonthly" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            // Valid token (refreshed if expired) + one retry after refresh on 401 — see AuthState.SendAsync
            using var resp = await _auth.SendAsync(_http, token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            }, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null; // session ended → popup via AuthState.SessionEnded
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<ForecastMonthlyDto>(cancellationToken: ct);
        }
    }
}
