using Mgt.Lit.WebFront.Auth;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net.Http.Headers;

namespace Mgt.Lit.WebFront.Services.Member
{
    public class ApiHttpClient
    {
        private readonly HttpClient _http;
        private readonly ProtectedSessionStorage _storage;
        private readonly AuthState _authState; // ✅ เพิ่ม

        public ApiHttpClient(HttpClient http, ProtectedSessionStorage storage, AuthState authState)
        {
            _http = http;
            _storage = storage;
            _authState = authState; // ✅ เพิ่ม
        }

        public async Task<HttpClient> CreateAsync()
        {
            _http.DefaultRequestHeaders.Authorization = null;

            // ✅ ใช้ AuthState.Token เป็นหลัก (อยู่ใน memory, always fresh)
            var token = _authState?.Token;

            // fallback ไป storage เฉพาะกรณี AuthState ยังไม่มีข้อมูล
            if (string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    var result = await _storage.GetAsync<string>("authToken");
                    token = result.Success ? result.Value : null;
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            return _http;
        }
    }
}