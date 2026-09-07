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
    private readonly ProtectedLocalStorage _localStorage;
    private readonly IHttpClientFactory _factory;

    // ✅ ลบ _httpContextAccessor ออกแล้ว ไม่ต้องใช้อีก
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<bool>? _refreshTask;

    public AuthTokenHandler(
        AuthState auth,
        ProtectedSessionStorage storage,
        ProtectedLocalStorage localStorage,
        IHttpClientFactory factory)
    // ✅ ลบ IHttpContextAccessor httpContextAccessor ออก
    {
        _auth = auth;
        _storage = storage;
        _localStorage = localStorage;
        _factory = factory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AttachToken(request, _auth.Token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        Console.WriteLine("[AuthTokenHandler] Got 401 → Starting refresh...");

        var refreshed = await RefreshWithQueueAsync(cancellationToken);

        if (!refreshed)
            return response;

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
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return await _refreshTask;

            _refreshTask = DoRefreshAsync(cancellationToken);
            return await _refreshTask;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ★ เช็ค session storage ก่อนเสมอ แล้วค่อย fallback ไป local storage — กัน "Remember me" (เก็บใน local storage
    //   แทน session storage) หา refreshToken ไม่เจอแล้ว refresh ล้มเหลวทั้งที่ token ยังไม่หมดอายุจริง
    private async Task<bool> DoRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rtResult = await _storage.GetAsync<string>("refreshToken");
            var refreshToken = rtResult.Success ? rtResult.Value : null;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                var localRtResult = await _localStorage.GetAsync<string>("refreshToken");
                refreshToken = localRtResult.Success ? localRtResult.Value : null;
            }

            Console.WriteLine($"[REFRESH] Token in storage: " +
                $"{(!string.IsNullOrWhiteSpace(refreshToken) ? "FOUND" : "NOT FOUND")}");

            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var client = _factory.CreateClient("RefreshClient");
            var response = await client.PostAsJsonAsync(
                "api/member/refresh-explicit",
                new { RefreshToken = refreshToken },
                cancellationToken);

            Console.WriteLine($"[REFRESH] Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content
                .ReadFromJsonAsync<RefreshResponse>(cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(result?.Token))
                return false;

            _auth.Token = result.Token;
            // ★ เขียนกลับไปที่ storage เดียวกับที่ใช้อยู่ (remembered -> local, ไม่งั้น -> session)
            var activeStorage = _auth.IsRemembered ? (ProtectedBrowserStorage)_localStorage : _storage;
            await activeStorage.SetAsync("authToken", result.Token);

            Console.WriteLine("[REFRESH] Success — new token saved");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REFRESH ERROR] {ex.Message}");
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

        foreach (var header in original.Headers)
        {
            if (header.Key == "Authorization") continue;
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

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