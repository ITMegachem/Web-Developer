using Mgt.Lit.WebFront.Models.Member;

namespace Mgt.Lit.WebFront.Services.Member
{
    public interface IAccountService
    {
        Task<(bool Ok, string? Error)> ChangePasswordAsync(ChangePasswordRequest req);
    }
}
