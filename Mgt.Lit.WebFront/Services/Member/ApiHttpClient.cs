using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net.Http.Headers;

namespace Mgt.Lit.WebFront.Services.Member
{
    public class ApiHttpClient
    {
        private readonly HttpClient _http;
        private readonly ProtectedSessionStorage _storage;

        public ApiHttpClient(HttpClient http, ProtectedSessionStorage storage)
        {
            _http = http;
            _storage = storage;
        }

        public async Task<HttpClient> CreateAsync()
        {
            var tokenResult = await _storage.GetAsync<string>("authToken");
            var token = tokenResult.Success ? tokenResult.Value : null;

            _http.DefaultRequestHeaders.Authorization = null;

            if (!string.IsNullOrWhiteSpace(token))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            return _http;
        }
    }
}