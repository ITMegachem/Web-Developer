using Mgt.Lit.Core.DTOs;
using System.Net.Http.Json;

namespace Mgt.Lit.WebFront.Services;

public class AuthService
{
    private readonly HttpClient _http;

    public AuthService(HttpClient http)
    {
        _http = http;
    }

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto req)
    {
        var response = await _http.PostAsJsonAsync(
            "api/member/login",
            req);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
                             .ReadFromJsonAsync<LoginResponseDto>();
    }
}
