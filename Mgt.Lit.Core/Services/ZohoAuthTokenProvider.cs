using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mgt.Lit.Core.Services
{
    // Cache Zoho OAuth access token ไว้ใช้ซ้ำข้ามการเรียกหลายๆ ครั้ง แทนที่จะขอ token ใหม่ทุกครั้ง
    // (access token อายุ ~1 ชม. — ตัดเผื่อ buffer 5 นาทีก่อนหมดอายุจริง กัน clock skew/request ที่กำลังค้างอยู่)
    //
    // ★ ต้อง register เป็น Singleton (builder.Services.AddSingleton<ZohoAuthTokenProvider>()) เพื่อให้ cache
    //   ใช้ร่วมกันได้จริงข้าม request — ถ้า register แบบ Scoped/Transient จะได้ instance ใหม่ทุกครั้งและ cache ไม่มีผล
    //   ใช้ HttpClient เป็น static field ของตัวเอง (ไม่พึ่ง DI) เพราะ AddHttpClient<T> ปกติให้ T เป็น Transient
    //   ซึ่งขัดกับการเป็น Singleton ของคลาสนี้
    //
    // appsettings.json (หรือ user-secrets) -> "ZohoConfig": { AccountsDomain, ClientId, ClientSecret, RefreshToken }
    public class ZohoAuthTokenProvider
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly SemaphoreSlim _lock = new(1, 1);

        private readonly IConfiguration _config;
        private readonly ILogger<ZohoAuthTokenProvider> _logger;

        private string? _accessToken;
        private string? _apiDomain;
        private DateTime _expireAtUtc = DateTime.MinValue;

        public ZohoAuthTokenProvider(IConfiguration config, ILogger<ZohoAuthTokenProvider> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<(string AccessToken, string ApiDomain)> GetAccessTokenAsync(CancellationToken ct = default)
        {
            if (IsValid()) return (_accessToken!, _apiDomain!);

            await _lock.WaitAsync(ct);
            try
            {
                // เช็คซ้ำหลังได้ lock เผื่อมีคนอื่น refresh เสร็จไปแล้วระหว่างที่เรารอคิว
                if (IsValid()) return (_accessToken!, _apiDomain!);

                var accountsDomain = _config["ZohoConfig:AccountsDomain"] ?? "https://accounts.zoho.com";
                var clientId = _config["ZohoConfig:ClientId"];
                var clientSecret = _config["ZohoConfig:ClientSecret"];
                var refreshToken = _config["ZohoConfig:RefreshToken"];

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(refreshToken))
                    throw new InvalidOperationException(
                        "Zoho OAuth credentials are not configured. Set ZohoConfig:ClientId / ClientSecret / RefreshToken (แนะนำผ่าน dotnet user-secrets).");

                var url = $"{accountsDomain.TrimEnd('/')}/oauth/v2/token" +
                          $"?refresh_token={Uri.EscapeDataString(refreshToken)}" +
                          $"&client_id={Uri.EscapeDataString(clientId)}" +
                          $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                          $"&grant_type=refresh_token";

                using var resp = await _http.PostAsync(url, content: null, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Zoho OAuth token refresh failed: {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("access_token", out var tokenEl))
                    throw new InvalidOperationException($"Zoho OAuth response missing access_token: {body}");

                _accessToken = tokenEl.GetString()!;
                _apiDomain = root.TryGetProperty("api_domain", out var apiDomainEl)
                    ? apiDomainEl.GetString() ?? "https://www.zohoapis.com"
                    : "https://www.zohoapis.com";

                var expiresIn = root.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var exp) ? exp : 3600;
                _expireAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300)); // เผื่อ 5 นาที

                _logger.LogInformation("[ZOHO] access token refreshed, valid until {ExpireAt:u}", _expireAtUtc);
                return (_accessToken, _apiDomain);
            }
            finally
            {
                _lock.Release();
            }
        }

        private bool IsValid() =>
            !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_apiDomain) && DateTime.UtcNow < _expireAtUtc;
    }
}
