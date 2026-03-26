using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth
{
    public static class JwtHelper
    {
        public static ClaimsPrincipal? ParseToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            var identity = new ClaimsIdentity(jwt.Claims, "jwt");
            return new ClaimsPrincipal(identity);
        }
    }
}
