using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Services.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace Mgt.Lit.WebApi.Controllers.DashBoard
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        private readonly ISalesOverviewService _salesOverviewService;
        private readonly IYearlyComparisonService _yearlyComparisonService;
        private readonly ISalePerformanceService _salePerformanceService;
        private readonly IProductOverviewService _productOverviewService;
        private readonly IProductMovementService _productMovementService;
        private readonly ICustomerOverviewService _customerOverviewService;
        private readonly IBillingDailyService _billingDailyService;


        public DashboardController(AppDbContext context,
            ISalesOverviewService salesOverviewService,
            IYearlyComparisonService yearlyComparisonService,
            ISalePerformanceService salePerformanceService,
            IProductOverviewService productOverviewService,
            IProductMovementService productMovementService,
            ICustomerOverviewService customerOverviewService,
            IBillingDailyService billingDailyService
            )
        {
            _context = context;

            _salesOverviewService = salesOverviewService;
            _yearlyComparisonService = yearlyComparisonService;
            _salePerformanceService = salePerformanceService;
            _productOverviewService = productOverviewService;
            _productMovementService = productMovementService;
            _customerOverviewService = customerOverviewService;
            _billingDailyService = billingDailyService;
        }
        // ---------- BU security: อ่านสิทธิ์จาก JWT claims (Division + DataScope) ----------
        // DataScope = COMPANY / CROSS_COMPANY -> เห็นทุก BU; DIVISION / OWN -> ล็อก BU ตัวเอง (Division)
        private string CurrentDataScope() =>
            (User.FindFirst("DataScope")?.Value ?? "OWN").Trim().ToUpperInvariant();

        private string CurrentDivision() =>
            User.FindFirst("Division")?.Value ?? "";

        private bool CanSeeAllBu()
        {
            var scope = CurrentDataScope();
            return scope is "COMPANY" or "CROSS_COMPANY" or "ALL" or "ALL_COMPANY" or "ALL_COMPANIES";
        }

        // BU2/3/4 (DIVISION/OWN) ถูกล็อกเป็น Division ตัวเองเสมอ; COMPANY/CROSS_COMPANY เลือกได้ (null = ทุก BU)
        private string? ResolveEffectiveBu(string? requestedBu) =>
            CanSeeAllBu() ? requestedBu : CurrentDivision();

        // GET api/dashboard/salesoverview?Year=2026&...
        [HttpGet("salesoverview")]
        public async Task<ActionResult<SalesOverviewDto>> GetSalesOverview([FromQuery] SalesOverviewFilter filter, CancellationToken ct)
        {
            var result = await _salesOverviewService.GetOverviewAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/yearlycomparison  (6 ปี ปีปัจจุบันย้อนหลัง 5 ปี, รายเดือน)
        [HttpGet("yearlycomparison")]
        public async Task<ActionResult<YearlyComparisonDto>> GetYearlyComparison(CancellationToken ct)
        {
            var result = await _yearlyComparisonService.GetAsync(ct);
            return Ok(result);
        }

        // GET api/dashboard/billingdaily?DateFrom=...&DateTo=...&CustomerFullName=...&SalesEmployeeBP=...
        [HttpGet("billingdaily")]
        public async Task<ActionResult<BillingDailyDto>> GetBillingDaily([FromQuery] BillingDailyFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _billingDailyService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/customeroverview?Year=2026&CustomerFullName=...&IndustryName=...
        [HttpGet("customeroverview")]
        public async Task<ActionResult<CustomerOverviewDto>> GetCustomerOverview([FromQuery] CustomerOverviewFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _customerOverviewService.GetAsync(filter, ct);

            result.AvailableBus = CanSeeAllBu()
                ? await _customerOverviewService.GetDistinctBusAsync(ct)   // เห็นทุก BU
                : new List<string> { CurrentDivision() };                 // แค่ BU ตัวเอง -> front ซ่อน dropdown
            result.EffectiveBu = filter.SalesGroup;
            return Ok(result);
        }

        // GET api/dashboard/productmovement?Year=2026&MaterialGroupName=...&CustomerFullName=...
        [HttpGet("productmovement")]
        public async Task<ActionResult<ProductMovementDto>> GetProductMovement([FromQuery] ProductMovementFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _productMovementService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/productoverview?Year=2026&MaterialGroupName=...
        [HttpGet("productoverview")]
        public async Task<ActionResult<ProductOverviewDto>> GetProductOverview([FromQuery] ProductOverviewFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _productOverviewService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/saleperformance?Year=2026&SalesEmployeeBP=...
        [HttpGet("saleperformance")]
        public async Task<ActionResult<SalePerformanceDto>> GetSalePerformance([FromQuery] SalePerformanceFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _salePerformanceService.GetAsync(filter, ct);

            result.AvailableBus = CanSeeAllBu()
                ? await _salePerformanceService.GetDistinctBusAsync(ct)
                : new List<string> { CurrentDivision() };
            result.EffectiveBu = filter.SalesGroup;
            return Ok(result);
        }
    }
}
