using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Services.Member;

namespace Mgt.Lit.WebFront.Services.DashBoard
{
    public class DashSalePerformanceApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;
        private readonly PageContext _pageContext;

        public DashSalePerformanceApiService(HttpClient http, AuthState auth, PageContext pageContext)
        {
            _http = http;
            _auth = auth;
            _pageContext = pageContext;
        }

        public async Task<SalePerformanceDto?> GetAsync(SalePerformanceFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.Year.HasValue) qs.Add($"Year={filter.Year}");
            if (!string.IsNullOrWhiteSpace(filter.SalesGroup)) qs.Add($"SalesGroup={Uri.EscapeDataString(filter.SalesGroup)}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");

            var url = "api/dashboard/saleperformance" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            using var request = CreateAuthorizedGet(url);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SalePerformanceDto>(cancellationToken: ct);
        }

        private HttpRequestMessage CreateAuthorizedGet(string url)
        {
            EnsureAuthenticated();

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);

            if (!string.IsNullOrWhiteSpace(_pageContext.Menu))
                request.Headers.TryAddWithoutValidation("X-Menu", _pageContext.Menu);
            if (!string.IsNullOrWhiteSpace(_pageContext.Page))
                request.Headers.TryAddWithoutValidation("X-Page", _pageContext.Page);

            return request;
        }

        private void EnsureAuthenticated()
        {
            if (string.IsNullOrWhiteSpace(_auth.Token))
                throw new UnauthorizedAccessException("Token is missing");
        }
    }
}
