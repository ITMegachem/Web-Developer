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
    public class DashProductOverviewApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;
        private readonly PageContext _pageContext;

        public DashProductOverviewApiService(HttpClient http, AuthState auth, PageContext pageContext)
        {
            _http = http;
            _auth = auth;
            _pageContext = pageContext;
        }

        public async Task<ProductOverviewDto?> GetAsync(ProductOverviewFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (filter.Year.HasValue) qs.Add($"Year={filter.Year}");
            if (!string.IsNullOrWhiteSpace(filter.MaterialGroupName)) qs.Add($"MaterialGroupName={Uri.EscapeDataString(filter.MaterialGroupName)}");
            if (!string.IsNullOrWhiteSpace(filter.MaterialName)) qs.Add($"MaterialName={Uri.EscapeDataString(filter.MaterialName)}");

            var url = "api/dashboard/productoverview" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            using var request = CreateAuthorizedGet(url);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProductOverviewDto>(cancellationToken: ct);
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