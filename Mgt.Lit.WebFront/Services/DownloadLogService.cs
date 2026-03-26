using Mgt.Lit.WebFront.Auth;

public class DownloadLogService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiBaseUrl;
    private readonly AuthState _authState;   // ✅ เพิ่ม

    public DownloadLogService(IHttpClientFactory httpFactory,
                              IConfiguration config,
                              AuthState authState)           // ✅ เพิ่ม
    {
        _httpFactory = httpFactory;
        _authState = authState;

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        _apiBaseUrl = env == "Development"
            ? config["ApiBaseUrlDev"]!
            : config["ApiBaseUrlDocker"]!;
    }

    public async Task LogDownloadAsync(string username, string fileName,
                                       string? ipAddress = null, string? userAgent = null)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            var payload = new { username, fileName, ipAddress, userAgent };
            var url = $"{_apiBaseUrl.TrimEnd('/')}/api/download-log";

            // ✅ แนบ JWT Token เหมือน API call อื่นๆ
            if (!string.IsNullOrEmpty(_authState.Token))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authState.Token);
            }

            var response = await client.PostAsJsonAsync(url, payload);
            Console.WriteLine($"[DownloadLog] Status: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadLog] Error: {ex.Message}");
        }
    }
}