using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth
{
    public static class PermissionHelper
    {
        private static bool IsAdmin(ClaimsPrincipal? user) =>
            string.Equals(
                user?.FindFirst(ClaimTypes.Role)?.Value,
                "admin",
                StringComparison.OrdinalIgnoreCase);

        public static bool HasPageAccess(ClaimsPrincipal? user, int pageNumber)
        {
            if (IsAdmin(user))
                return true;

            var claimType = pageNumber switch
            {
                1 => "Page1Access",
                2 => "Page2Access",
                3 => "Page3Access",
                4 => "Page4Access",
                _ => string.Empty
            };

            return HasBooleanClaim(user, claimType);
        }

        public static bool CanAccessRoute(ClaimsPrincipal? user, string? relativePath)
        {
            if (IsAdmin(user))
                return true;

            var path = NormalizePath(relativePath);

            return path switch
            {
                "sales-orders" => HasPageAccess(user, 1),
                "warehouse-stock-material" => HasPageAccess(user, 2),
                "warehouse-stock-movement" => HasPageAccess(user, 3),
                "nof-report" => HasPageAccess(user, 4),
                "dashboard" => true,
                "main" => true,
                "profile" => true,
                "unauthorized" => true,
                "" => true,
                _ => true
            };
        }

        public static bool HasBooleanClaim(ClaimsPrincipal? user, string claimType)
        {
            if (string.IsNullOrWhiteSpace(claimType))
                return false;

            var value = user?.FindFirst(claimType)?.Value;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value, out var result))
                return result;

            return value == "1" ||
                   value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            var path = relativePath.Trim().Trim('/');

            var queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
                path = path[..queryIndex];

            return path.ToLowerInvariant();
        }
    }
}