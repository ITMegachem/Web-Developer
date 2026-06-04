// Mgt.Lit.WebApi/Filters/RequirePageAccessAttribute.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace Mgt.Lit.WebApi.Filters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class RequirePageAccessAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _pageClaim;

        public RequirePageAccessAttribute(string pageClaim = "Page2Access")
        {
            _pageClaim = pageClaim;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // ── 1. เช็คว่า login อยู่หรือเปล่า ──────────────────────────────
            var user = context.HttpContext.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            // ── 2. เช็ค Claim ที่ระบุ ─────────────────────────────────────────
            var claim = user.FindFirst(_pageClaim)?.Value;
            if (!string.Equals(claim, "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new ForbidResult();
                return;
            }
        }
    }
}