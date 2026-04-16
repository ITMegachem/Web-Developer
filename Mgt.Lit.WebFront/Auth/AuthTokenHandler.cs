// AuthTokenHandler.cs
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Mgt.Lit.WebFront.Auth;

public class AuthTokenHandler : DelegatingHandler
{
    private readonly AuthState _auth;
    private readonly ProtectedSessionStorage _storage;
    private readonly IHttpClientFactory _factory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // ✅ Queue สำหรับกัน refresh ซ้อนกัน
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);
    private static Task<bool>? _refreshTask;

    public AuthTokenHandler(
        AuthState auth,
        ProtectedSessionStorage storage,
        IHttpClientFactory factory,
        IHttpContextAccessor httpContextAccessor)
    {
        _auth = auth;
        _storage = storage;
        _factory = factory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[AuthTokenHandler] Sending: {request.RequestUri}");
        // ✅ ใส่ token ปัจจุบันก่อนส่ง
        AttachToken(request, _auth.Token);

        var response = await base.SendAsync(request, cancellationToken);
        Console.WriteLine($"[AuthTokenHandler] Status: {response.StatusCode}");
        // ✅ ถ้าไม่ใช่ 401 → return ปกติ
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;
        Console.WriteLine("[AuthTokenHandler] Got 401 → Starting refresh...");
        // ✅ ถ้า 401 → รอ refresh (ถ้ามีคนกำลัง refresh อยู่ ให้รอ)
        var refreshed = await RefreshWithQueueAsync(cancellationToken);
        Console.WriteLine($"[AuthTokenHandler] Refresh result: {refreshed}");
        if (!refreshed)
            return response; // refresh ไม่ได้ → return 401 ให้ caller จัดการ

        // ✅ Retry request เดิมด้วย token ใหม่
        // ต้อง clone เพราะ HttpRequestMessage ใช้ซ้ำไม่ได้
        Console.WriteLine("[AuthTokenHandler] Retrying request with new token...");
        using var retryRequest = await CloneRequestAsync(request);
        AttachToken(retryRequest, _auth.Token);

        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private async Task<bool> RefreshWithQueueAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // ✅ ถ้ามี refresh task ที่กำลังทำอยู่ → รอผลแทน
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return await _refreshTask;

            // ✅ สร้าง refresh task ใหม่
            _refreshTask = DoRefreshAsync(cancellationToken);
            return await _refreshTask;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // AuthTokenHandler.cs
    private async Task<bool> DoRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _factory.CreateClient("RefreshClient");

            // ✅ ดึง refreshToken cookie จาก Browser request
            var refreshCookie = _httpContextAccessor.HttpContext?
                .Request.Cookies["refreshToken"];

            Console.WriteLine($"[AuthTokenHandler] refreshToken cookie: {(string.IsNullOrEmpty(refreshCookie) ? "NOT FOUND" : "FOUND")}");

            if (string.IsNullOrWhiteSpace(refreshCookie))
                return false;

            // ✅ ส่ง Cookie ไปกับ request
            var request = new HttpRequestMessage(HttpMethod.Post, "api/member/refresh");
            request.Headers.TryAddWithoutValidation("Cookie", $"refreshToken={refreshCookie}");

            var response = await client.SendAsync(request, cancellationToken);
            Console.WriteLine($"[AuthTokenHandler] Refresh status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content
                .ReadFromJsonAsync<RefreshResponse>(cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(result?.Token))
                return false;

            _auth.Token = result.Token;
            await _storage.SetAsync("authToken", result.Token);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthTokenHandler] Refresh exception: {ex.Message}");
            return false;
        }
        finally
        {
            _refreshTask = null;
        }
    }

    private static void AttachToken(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        // Copy headers ยกเว้น Authorization (จะใส่ใหม่)
        foreach (var header in original.Headers)
        {
            if (header.Key == "Authorization") continue;
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content
        if (original.Content != null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class RefreshResponse
    {
        public string? Token { get; set; }
    }
}