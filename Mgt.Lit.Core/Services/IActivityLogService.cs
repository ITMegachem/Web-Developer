using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Services
{
    public interface IActivityLogService
    {
        Task LogAsync(
        int userId,
        string username,
        int? companyId,
        string menu,
        string page,
        string action,
        long executionTimeMs,
        string ipAddress,
        int statusCode,
        bool isSuccess
    );
    }
}
