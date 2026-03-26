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
            Console.WriteLine("FILTER TRIGGERED");
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
                var menu = context.HttpContext.Request.Headers["X-Menu"].FirstOrDefault();

                var page = context.HttpContext.Request.Headers["X-Page"].FirstOrDefault();
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
                var statusCode = context.HttpContext.Response.StatusCode;

                var finalMenu = string.IsNullOrEmpty(menu)
                 ? context.Controller.GetType().Name
                 : menu;

                var finalPage = string.IsNullOrEmpty(page)
                    ? context.ActionDescriptor.DisplayName
                    : page;

                await logService.LogAsync(
                    userId,
                    username,
                    null,
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
