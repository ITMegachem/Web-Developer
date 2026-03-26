using Microsoft.JSInterop;
using System.Net.Http.Headers;

namespace Mgt.Lit.WebFront.Services
{
    public class ApiHttpClient
    {
        private readonly HttpClient _http;
        private readonly IJSRuntime _js;

        public ApiHttpClient(HttpClient http, IJSRuntime js)
        {
            _http = http;
            _js = js;
        }

        public async Task<HttpClient> CreateAsync()
        {
            var token = await _js.InvokeAsync<string>(
                "localStorage.getItem", "token");

            if (!string.IsNullOrEmpty(token))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            return _http;
        }


    }
}
