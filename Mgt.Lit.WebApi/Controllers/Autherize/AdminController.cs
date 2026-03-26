using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mgt.Lit.WebApi.Controllers.Autherize
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "admin")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // =====================================
        // POST: /api/admin/dashboard
        // =====================================
        [HttpPost("dashboard")]
        public async Task<IActionResult> GetDashboard([FromBody] DashboardRequestDto dto)
        {
            var query = _context.ActivityLogs.AsQueryable();

            if (dto.DateFrom.HasValue)
                query = query.Where(x => x.CreateDate >= dto.DateFrom.Value);

            if (dto.DateTo.HasValue)
                query = query.Where(x => x.CreateDate <= dto.DateTo.Value);

            var total = await query.CountAsync();
            var success = await query.CountAsync(x => x.IsSuccess == true);
            var fail = await query.CountAsync(x => x.IsSuccess == false);
            var avgTime = total > 0
                ? await query.AverageAsync(x => x.ExecutionTimeMs)
                : 0;

            return Ok(new
            {
                TotalRequests = total,
                SuccessCount = success,
                FailCount = fail,
                AvgExecutionTime = avgTime
            });
        }

        // =====================================
        // POST: /api/admin/activity
        // =====================================
        [HttpPost("activity")]
        public async Task<IActionResult> GetActivity([FromBody] ActivityFilterDto dto)
        {
            var query = _context.ActivityLogs.AsQueryable();

            if (dto.DateFrom.HasValue)
                query = query.Where(x => x.CreateDate >= dto.DateFrom.Value);

            if (dto.DateTo.HasValue)
                query = query.Where(x => x.CreateDate <= dto.DateTo.Value);

            if (!string.IsNullOrEmpty(dto.Username))
                query = query.Where(x => x.Username == dto.Username);

            if (dto.IsSuccess.HasValue)
                query = query.Where(x => x.IsSuccess == dto.IsSuccess.Value);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.CreateDate)
                .Skip((dto.Page - 1) * dto.PageSize)
                .Take(dto.PageSize)
                .Select(x => new
                {
                    x.Username,
                    x.Menu,
                    x.Page,
                    x.ExecutionTimeMs,
                    x.IpAddress,
                    x.IsSuccess,
                    x.CreateDate
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                dto.Page,
                dto.PageSize,
                items
            });
        }

        // =====================================
        // POST: /api/admin/error
        // =====================================
        [HttpPost("error")]
        public async Task<IActionResult> GetError([FromBody] ErrorFilterDto dto)
        {
            var query = _context.ErrorLogs.AsQueryable();

            if (dto.DateFrom.HasValue)
                query = query.Where(x => x.CreateDate >= dto.DateFrom.Value);

            if (dto.DateTo.HasValue)
                query = query.Where(x => x.CreateDate <= dto.DateTo.Value);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.CreateDate)
                .Skip((dto.Page - 1) * dto.PageSize)
                .Take(dto.PageSize)
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                dto.Page,
                dto.PageSize,
                items
            });
        }

        // =====================================
        // POST: /api/admin/monitoring
        // =====================================
        [HttpPost("monitoring")]
        public async Task<IActionResult> GetMonitoring()
        {
            var last24h = DateTime.UtcNow.AddHours(-24);

            var logs = await _context.ActivityLogs
                .Where(x => x.CreateDate >= last24h)
                .ToListAsync();

            var slowApis = logs
                .Where(x => x.ExecutionTimeMs > 3000)
                .OrderByDescending(x => x.ExecutionTimeMs)
                .Take(10);

            var errorRate = logs.Count > 0
                ? (double)logs.Count(x => x.IsSuccess == false) / logs.Count * 100
                : 0;

            return Ok(new
            {
                Last24HoursTotal = logs.Count,
                ErrorRatePercent = Math.Round(errorRate, 2),
                SlowApis = slowApis
            });
        }
    }
}
