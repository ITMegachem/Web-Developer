using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Services.Member;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using static Mgt.Lit.WebFront.Constants.ApiRoutes;

namespace Mgt.Lit.WebFront.Services.DashBoard
{
    public class DashYearlyComparisonApiService
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;
        private readonly PageContext _pageContext;


        public DashYearlyComparisonApiService(HttpClient http, AuthState auth, PageContext pageContext)
        {
            _http = http;
            _auth = auth;
            _pageContext = pageContext;

        }

        public async Task<YearlyComparisonDto?> GetAsync(CancellationToken ct = default)
        {
            using var request = CreateAuthorizedGet("api/dashboard/yearlycomparison");
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<YearlyComparisonDto>(cancellationToken: ct);
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