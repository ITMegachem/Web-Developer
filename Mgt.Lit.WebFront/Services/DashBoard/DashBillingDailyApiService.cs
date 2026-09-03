using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class DashBillingDailyApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;
        private readonly PageContext _pageContext;

        public DashBillingDailyApiService(HttpClient http, AuthState auth, PageContext pageContext)
        {
            _http = http;
            _auth = auth;
            _pageContext = pageContext;
        }

        public async Task<BillingDailyDto?> GetAsync(BillingDailyFilter filter, CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter.CustomerFullName)) qs.Add($"CustomerFullName={Uri.EscapeDataString(filter.CustomerFullName)}");
            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeBP)) qs.Add($"SalesEmployeeBP={Uri.EscapeDataString(filter.SalesEmployeeBP)}");
            if (filter.DateFrom.HasValue) qs.Add($"DateFrom={filter.DateFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            if (filter.DateTo.HasValue) qs.Add($"DateTo={filter.DateTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

            var url = "api/dashboard/billingdaily" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            using var request = CreateAuthorizedGet(url);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BillingDailyDto>(cancellationToken: ct);
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