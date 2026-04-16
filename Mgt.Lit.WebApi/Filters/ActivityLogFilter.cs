using Mgt.Lit.Core.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace Mgt.Lit.WebApi.Filters
{
    public class ActivityLogFilter : IAsyncActionFilter
{
    private readonly IActivityLogService _logService;
    private readonly IServiceScopeFactory _scopeFactory;
        public ActivityLogFilter(IActivityLogService logService, IServiceScopeFactory scopeFactory)
        {
            _logService = logService;
            _scopeFactory = scopeFactory;
        }

        public async Task OnActionExecutionAsync(
     ActionExecutingContext context,
     ActionExecutionDelegate next)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var executedContext = await next();
            stopwatch.Stop();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logService = scope.ServiceProvider.GetRequiredService<IActivityLogService>();

                var user = context.HttpContext.User;
                var username = user.Identity?.Name ?? "Anonymous";

                var userIdClaim = user.FindFirst("UserID")?.Value;
                int.TryParse(userIdClaim, out var userId);

                // ✅ อ่าน CompanyID จาก Claim
                var companyIdClaim = user.FindFirst("CompanyID")?.Value;
                int? companyId = int.TryParse(companyIdClaim, out var cid) ? cid : null;

                var menu = context.HttpContext.Request.Headers["X-Menu"].FirstOrDefault();
                var page = context.HttpContext.Request.Headers["X-Page"].FirstOrDefault();
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();

                // ✅ อ่าน StatusCode จาก executedContext
                var statusCode = executedContext.HttpContext.Response.StatusCode;
                if (executedContext.Exception != null && !executedContext.ExceptionHandled)
                    statusCode = 500;

                var finalMenu = string.IsNullOrEmpty(menu)
                    ? context.Controller.GetType().Name
                    : menu;

                var finalPage = string.IsNullOrEmpty(page)
                    ? context.ActionDescriptor.DisplayName
                    : page;

                await logService.LogAsync(
                    userId,
                    username,
                    companyId,  // ✅
                    finalMenu,
                    finalPage,
                    context.HttpContext.Request.Method,
                    stopwatch.ElapsedMilliseconds,
                    ip,
                    statusCode,
                    statusCode < 400
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine("LOG ERROR: " + ex.Message);
            }
        }
    }
}
