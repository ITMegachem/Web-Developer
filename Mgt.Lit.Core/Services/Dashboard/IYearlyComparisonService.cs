using Mgt.Lit.Core.DTOs.DashBoard;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface IYearlyComparisonService
    {
        Task<YearlyComparisonDto> GetAsync(string? salesGroup, string? salesEmployeeBP, CancellationToken ct = default);
    }
}
