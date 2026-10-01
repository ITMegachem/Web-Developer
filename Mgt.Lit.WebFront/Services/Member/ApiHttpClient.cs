using Mgt.Lit.WebFront.Auth;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net.Http.Headers;

namespace Mgt.Lit.WebFront.Services.Member
{
    public class ApiHttpClient
    {
        private readonly HttpClient _http;
        private readonly ProtectedLocalStorage _storage;
        private readonly AuthState _authState; // ✅ เพิ่ม

        public ApiHttpClient(HttpClient http, ProtectedLocalStorage storage, AuthState authState)
        {
            _http = http;
            _storage = storage;
            _authState = authState; // ✅ เพิ่ม
        }

        public async Task<HttpClient> CreateAsync()
        {
            _http.DefaultRequestHeaders.Authorization = null;

            // ✅ Token ที่ยังไม่หมดอายุ (refresh ให้ถ้าหมดแล้ว — token อยู่ใน localStorage ข้ามการปิดเบราว์เซอร์ได้)
            string? token = null;
            try { token = _authState != null ? await _authState.GetValidTokenAsync() : null; } catch { }
            token ??= _authState?.Token;

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