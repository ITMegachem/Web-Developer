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
        private readonly IPricingMarginPerformanceService _pricingMarginPerformanceService;
        private readonly ICustomerChurnAnalysisService _customerChurnAnalysisService;
        private readonly IOpportunityWinRateService _opportunityWinRateService;
        private readonly IAverageDaysToCloseService _averageDaysToCloseService;
        private readonly ICrossSellUpsellGainsService _crossSellUpsellGainsService;
        private readonly ISalesForecastAccuracyService _salesForecastAccuracyService;
        private readonly IVisitDailyReportService _visitDailyReportService;
        private readonly ISalesProductivityService _salesProductivityService;
        private readonly IForecastMonthlyReportService _forecastMonthlyReportService;


        public DashboardController(AppDbContext context,
            ISalesOverviewService salesOverviewService,
            IYearlyComparisonService yearlyComparisonService,
            ISalePerformanceService salePerformanceService,
            IProductOverviewService productOverviewService,
            IProductMovementService productMovementService,
            ICustomerOverviewService customerOverviewService,
            IBillingDailyService billingDailyService,
            IPricingMarginPerformanceService pricingMarginPerformanceService,
            ICustomerChurnAnalysisService customerChurnAnalysisService,
            IOpportunityWinRateService opportunityWinRateService,
            IAverageDaysToCloseService averageDaysToCloseService,
            ICrossSellUpsellGainsService crossSellUpsellGainsService,
            ISalesForecastAccuracyService salesForecastAccuracyService,
            IVisitDailyReportService visitDailyReportService,
            ISalesProductivityService salesProductivityService,
            IForecastMonthlyReportService forecastMonthlyReportService
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
            _pricingMarginPerformanceService = pricingMarginPerformanceService;
            _customerChurnAnalysisService = customerChurnAnalysisService;
            _opportunityWinRateService = opportunityWinRateService;
            _averageDaysToCloseService = averageDaysToCloseService;
            _salesForecastAccuracyService = salesForecastAccuracyService;
            _crossSellUpsellGainsService = crossSellUpsellGainsService;
            _visitDailyReportService = visitDailyReportService;
            _salesProductivityService = salesProductivityService;
            _forecastMonthlyReportService = forecastMonthlyReportService;
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

        // GET api/dashboard/pricingmarginperformance?Year=2026&TargetMarginPercent=30
        [HttpGet("pricingmarginperformance")]
        public async Task<ActionResult<PricingMarginPerformanceDto>> GetPricingMarginPerformance([FromQuery] PricingMarginPerformanceFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _pricingMarginPerformanceService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/customerchurnanalysis?Year=2026
        [HttpGet("customerchurnanalysis")]
        public async Task<ActionResult<CustomerChurnAnalysisDto>> GetCustomerChurnAnalysis([FromQuery] CustomerChurnAnalysisFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _customerChurnAnalysisService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/opportunitywinrate?DateFrom=2026-01-01&DateTo=2026-05-31
        [HttpGet("opportunitywinrate")]
        public async Task<ActionResult<OpportunityWinRateDto>> GetOpportunityWinRate([FromQuery] OpportunityWinRateFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _opportunityWinRateService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/averagedaystoclose?DateFrom=2026-01-01&DateTo=2026-05-31
        [HttpGet("averagedaystoclose")]
        public async Task<ActionResult<AverageDaysToCloseDto>> GetAverageDaysToClose([FromQuery] AverageDaysToCloseFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _averageDaysToCloseService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/crosssellupsellgains?DateFrom=2024-01-01&DateTo=2024-05-31
        [HttpGet("crosssellupsellgains")]
        public async Task<ActionResult<CrossSellUpsellGainsDto>> GetCrossSellUpsellGains([FromQuery] CrossSellUpsellGainsFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _crossSellUpsellGainsService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/salesforecastaccuracy?DateFrom=2024-06-01&DateTo=2024-05-31
        [HttpGet("salesforecastaccuracy")]
        public async Task<ActionResult<SalesForecastAccuracyDto>> GetSalesForecastAccuracy([FromQuery] SalesForecastAccuracyFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _salesForecastAccuracyService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/visitdailyreport?DateFrom=2026-08-01&DateTo=2026-08-31
        [HttpGet("visitdailyreport")]
        public async Task<ActionResult<VisitDailyReportDto>> GetVisitDailyReport([FromQuery] VisitDailyReportFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _visitDailyReportService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/salesproductivity?DateFrom=2026-01-01&DateTo=2026-09-30
        [HttpGet("salesproductivity")]
        public async Task<ActionResult<SalesProductivityDto>> GetSalesProductivity([FromQuery] SalesProductivityFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _salesProductivityService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/forecastmonthly?FromMonth=2026-05-01&ToMonth=2026-12-01
        [HttpGet("forecastmonthly")]
        public async Task<ActionResult<ForecastMonthlyDto>> GetForecastMonthly([FromQuery] ForecastMonthlyFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            var result = await _forecastMonthlyReportService.GetAsync(filter, ct);
            return Ok(result);
        }
    }
}
