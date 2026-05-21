using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mgt.Lit.Core.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly AppDbContext _context;

        public ActivityLogService(AppDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            int userId,
            string username,
            int? companyId,
            string menu,
            string page,
            string action,
            long executionTimeMs,
            string ipAddress,
            int statusCode,
            bool isSuccess)
        {
            if (userId == 0 || username == "Anonymous")
            {
                return;
            }
            try
            {
                Console.WriteLine("LOG SAVING...");

                // ✅ 1. ดึง Department จาก View_UserPermission
                var userPermission = await _context.View_UserPermissions
                    .FirstOrDefaultAsync(u => u.UserID == userId);

                string? department = userPermission?.Department;

                // ✅ 2. คำนวณ Session Duration (นาที)
                //    หา Log ล่าสุดของ User นี้
                double? sessionDurationMinutes = null;

                var lastLog = await _context.ActivityLogs
                    .Where(l => l.UserID == userId)
                    .OrderByDescending(l => l.CreateDate)
                    .FirstOrDefaultAsync();

                if (lastLog != null)
                {
                    var duration = DateTime.UtcNow - lastLog.CreateDate;
                    // ถ้า gap ไม่เกิน 30 นาที ถือว่ายัง Session เดิม
                    if (duration.TotalMinutes <= 30)
                    {
                        sessionDurationMinutes = Math.Round(duration.TotalMinutes, 2);
                    }
                }

                var log = new ActivityLog
                {
                    SystemID = Guid.NewGuid(),
                    UserID = userId,
                    Username = username,
                    CompanyID = companyId,
                    Menu = menu,
                    Page = page,
                    Action = action,
                    ExecutionTimeMs = (int)executionTimeMs,
                    IpAddress = ipAddress,
                    StatusCode = statusCode,
                    IsSuccess = isSuccess,
                    CreateDate = DateTime.UtcNow,
                    // ✅ เพิ่มใหม่
                    Department = department,
                    SessionDurationMinutes = sessionDurationMinutes
                };

                _context.ActivityLogs.Add(log);

                Console.WriteLine($"UserID     = {userId}");
                Console.WriteLine($"Username   = {username}");
                Console.WriteLine($"Department = {department}");
                Console.WriteLine($"SessionDur = {sessionDurationMinutes} min");
                Console.WriteLine($"Menu       = {menu}");
                Console.WriteLine($"Page       = {page}");

                await _context.SaveChangesAsync();
                Console.WriteLine("LOG SAVED");
            }
            catch (Exception ex)
            {
                Console.WriteLine("LOG ERROR: " + ex.Message);
            }
        }
    }
}