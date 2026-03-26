using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly AppDbContext _context;

        public ActivityLogService(AppDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(int userId,string username,int? companyId,string menu,string page,string action,long executionTimeMs,string ipAddress,int statusCode,bool isSuccess)
        {
            try
            {
                Console.WriteLine("LOG SAVING...");
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
                    CreateDate = DateTime.UtcNow
                };

                _context.ActivityLogs.Add(log);
                Console.WriteLine($"UserID = {userId}");
                Console.WriteLine($"Username = {username}");
                Console.WriteLine("Menu: " + menu);
                Console.WriteLine("Page: " + page);
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
