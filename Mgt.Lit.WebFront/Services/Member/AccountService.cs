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
            var http = await _api.CreateAsync();

            var res = await http.PostAsJsonAsync("api/member/change-password", req);

            if (res.IsSuccessStatusCode)
                return (true, null);

            var body = await res.Content.ReadAsStringAsync();
            return (false, $"{(int)res.StatusCode} {res.ReasonPhrase} {body}");
        }
    }
}
