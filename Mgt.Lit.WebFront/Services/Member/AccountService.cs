using Mgt.Lit.WebFront.Models.Member;
using System.Net.Http.Json;

namespace Mgt.Lit.WebFront.Services.Member
{
    public class AccountService : IAccountService
    {
        private readonly ApiHttpClient _api;

        public AccountService(ApiHttpClient api)
        {
            _api = api;
        }

        public async Task<(bool Ok, string? Error)> ChangePasswordAsync(ChangePasswordRequest req)
        {
            try
            {
                var http = await _api.CreateAsync();

                Console.WriteLine($"[ChangePassword] BaseAddress: {http.BaseAddress}");
                Console.WriteLine($"[ChangePassword] Auth: {http.DefaultRequestHeaders.Authorization?.Parameter?.Substring(0, 20)}...");

                var res = await http.PostAsJsonAsync("api/member/change-password", req);

                var body = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"[ChangePassword] Status: {(int)res.StatusCode}");
                Console.WriteLine($"[ChangePassword] Body: {body}");

                if (res.IsSuccessStatusCode)
                    return (true, null);

                return (false, body);
            }
            catch (Exception ex)
            {
                // ✅ ดู exception จริง
                Console.WriteLine($"[ChangePassword] EXCEPTION: {ex.GetType().Name}");
                Console.WriteLine($"[ChangePassword] MESSAGE: {ex.Message}");
                Console.WriteLine($"[ChangePassword] INNER: {ex.InnerException?.Message}");

                return (false, $"Exception: {ex.Message}");
            }
        }
    }
}
