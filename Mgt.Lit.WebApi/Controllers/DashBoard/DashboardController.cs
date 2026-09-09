using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs.DashBoard;
using Mgt.Lit.Core.Services.Dashboard;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace Mgt.Lit.WebApi.Controllers.DashBoard
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize]
    [ServiceFilter(typeof(ActivityLogFilter))]
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

        // ---------- Position-based security เฉพาะ Opportunity Win Rate Report (ของเดิม) ----------
        // 1) Position = CMO/CFO/IT      -> เห็นข้อมูลทั้งหมด (ไม่ล็อกทั้ง BU และ Salesperson)
        // 2) UserRole = Leader/Manager/Admin -> เห็นได้ทั้ง BU ตัวเอง (ควบคุมด้วย ResolveEffectiveBu อยู่แล้ว) ไม่ล็อกลงไปถึงระดับบุคคล
        // 3) นอกนั้น (พนักงานขายทั่วไป, Position=Sales + UserRole=user) -> เห็นเฉพาะข้อมูลของตัวเองเท่านั้น
        private string CurrentPosition() =>
            (User.FindFirst("Position")?.Value ?? "").Trim().ToUpperInvariant();

        // ★ เผื่อ token บางเคส (เช่นผ่าน SSO) ไม่ map short claim "role" กลับเป็น ClaimTypes.Role ให้อัตโนมัติ
        // ดู pattern เดียวกันที่ SideMenu.razor (ฝั่ง WebFront) ใช้อยู่แล้วสำหรับปัญหานี้เป๊ะๆ — normalize เป็นตัวพิมพ์ใหญ่
        // เพื่อกันปัญหา casing ไม่ตรงกัน (DB เก็บ "Manager"/"manager"/"MANAGER" ปนกันได้)
        private string CurrentUserRole() =>
            (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? User.FindFirst("userRole")?.Value
                ?? User.FindFirst("role")?.Value
                ?? "").Trim().ToUpperInvariant();

        private string CurrentFullName() =>
            User.FindFirst("FullName")?.Value ?? "";

        private string CurrentUsername() =>
            User.Identity?.Name ?? "";

        private string CurrentDepartment() =>
            (User.FindFirst("Department")?.Value ?? "").Trim().ToUpperInvariant();

        private bool HasFullAccessPosition() =>
            CurrentPosition() is "CMO" or "CFO" or "IT";

        // ---------- Sales Intelligence Matrix permissions (Department + UserRole จาก JWT) ----------
        // อ้างอิงจาก query คัดกรอง user ที่มีสิทธิ์เข้ารายงานกลุ่มนี้: Department IN (Sales, CSR, IT) + IsActive=1 +
        // SalesOrganization=1000 หรือ username พิเศษ 3 คนด้านล่าง (ได้สิทธิเต็มเสมอไม่ว่า Department จริงจะเป็นอะไร)
        // ลำดับการตัดสิน (จากเข้มงวดสุดไปหลวมสุด — เช็ค username พิเศษและ CSR ก่อน UserRole เสมอ):
        //   1) Username พิเศษ                  -> เห็นทุกรายงาน + Export ได้ทุกรายงาน
        //   2) Department = CSR                -> เห็นเฉพาะ Sales Forecast Accuracy + Forecast Monthly เท่านั้น
        //      (เช็คก่อนข้อ 3 เสมอ — เป็นข้อจำกัดตามหน้าที่งาน ไม่ใช่ตามตำแหน่ง แม้ CSR จะมี UserRole=Manager ก็ยังถูกจำกัด)
        //   3) UserRole = Manager/Admin หรือ Department = IT -> เห็นทุกรายงาน + Export ได้ทุกรายงาน
        //   4) Department = Sales, UserRole = Leader -> เห็นทุกรายงาน, ล็อกเฉพาะ Division ตัวเอง (บังคับผ่าน
        //      ResolveEffectiveBuForMatrix โดยตรง ไม่พึ่ง DataScope claim เพราะเป็นคนละระบบสิทธิ์กับหน้า Overview), Export ได้
        //   5) Department = Sales, UserRole อื่นๆ (พนักงานขายทั่วไป) -> เห็นทุกรายงาน, ล็อกเฉพาะข้อมูลตัวเอง,
        //      Export ไม่ได้ยกเว้น Visit Report
        //   6) นอกเหนือจากนี้ (ไม่อยู่ใน Sales/CSR/IT และไม่ใช่ username พิเศษ) -> ไม่มีสิทธิ์เข้ารายงานกลุ่มนี้เลย
        private static readonly HashSet<string> FullAccessUsernames = new(System.StringComparer.OrdinalIgnoreCase)
        { "MGT000298", "MGT000297", "MGT000018" };

        private static readonly HashSet<string> CsrAllowedReports = new(System.StringComparer.OrdinalIgnoreCase)
        { "SalesForecastAccuracy", "ForecastMonthly" };

        private bool IsSpecialFullAccessUser() =>
            FullAccessUsernames.Contains(CurrentUsername());

        private bool IsCsrRestricted() =>
            !IsSpecialFullAccessUser() && CurrentDepartment() == "CSR";

        private bool IsEligibleForMatrix() =>
            IsSpecialFullAccessUser() || CurrentDepartment() is "SALES" or "CSR" or "IT";

        private bool IsFullAccessUser() =>
            IsSpecialFullAccessUser()
            || HasFullAccessPosition()
            || CurrentDepartment() == "IT"
            || CurrentUserRole() is "MANAGER" or "ADMIN";

        // เรียกที่ต้นแต่ละ action ของ 9 รายงานใน Sales Intelligence Matrix — คืน true ถ้าดูรายงานนี้ได้
        // ★ เช็ค IsFullAccessUser ก่อนเสมอ (ข้อ 1/3 ของกติกา) — Manager/Admin/IT ไม่ผูกกับ Department ใน
        // IsEligibleForMatrix เลย ต้องข้ามด่านนั้นไปได้เต็มๆ ไม่งั้น Manager/Admin ที่ Department ไม่ใช่ Sales/CSR/IT
        // (เช่นว่าง หรือแผนกอื่น) จะโดนบล็อกทุกรายงานทั้งที่ควรเห็นทั้งหมด
        private bool CanAccessReport(string reportKey)
        {
            if (IsFullAccessUser()) return true;
            if (!IsEligibleForMatrix()) return false;
            if (IsCsrRestricted()) return CsrAllowedReports.Contains(reportKey);
            return true;
        }

        // ★ MGT_Deal.SalesEmployeeBP มาจาก Zoho Owner.name ซึ่งบางองค์กรตั้งเป็นแค่ชื่อ/นามสกุลบางส่วน ไม่ใช่ชื่อเต็ม
        // จึงจับคู่แบบ "ชื่อเต็มของ user contains ค่าที่ตั้งใน Zoho" แทนการเทียบเท่ากันตรงๆ (ดูตรรกะเดียวกันฝั่ง
        // OpportunityWinRateService.ApplyScopeFilter) — ทับค่าที่ client ส่งมาเสมอเมื่อ role ต้องถูกล็อก กัน spoof
        // ใช้ร่วมกันทุกรายงานที่อิง Zoho Deal (SalesEmployeeBP) — ส่วนรายงานที่อิง SAP (MGT_Sale) คอลัมน์เป็นชื่อเต็มอยู่แล้ว
        // จึงใช้ค่าเดียวกันนี้เทียบตรงๆ ได้เลยไม่ต้องปรับ (ดู ApplyCommonFilter/ApplyScopeFilter ของแต่ละ service)
        private string? ResolveEffectiveSalesEmployeeFilter(string? requestedSalesEmployee)
        {
            if (IsFullAccessUser()) return requestedSalesEmployee;

            var role = CurrentUserRole();
            if (role is "LEADER") return requestedSalesEmployee;

            // ★ ล็อกเฉพาะข้อมูลตัวเองเฉพาะ Department=Sales + UserRole=User (พนักงานขายทั่วไป) เท่านั้น — คนแผนกอื่น
            // (เช่น CSR ที่มีสิทธิ์เข้า SalesForecastAccuracy/ForecastMonthly ได้) ไม่ใช่ salesperson ชื่อเขาจะไม่มีทาง
            // ตรงกับ SalesEmployeeBP เลย ถ้าล็อกด้วยชื่อตัวเองจะเห็นข้อมูลว่างเปล่าผิดๆ จึงปล่อยผ่านให้เห็นตามสิทธิ์ BU แทน
            if (CurrentDepartment() != "SALES") return requestedSalesEmployee;

            var fullName = CurrentFullName();
            return string.IsNullOrWhiteSpace(fullName) ? requestedSalesEmployee : fullName;
        }

        // ★ ตัวเลือกใน dropdown "Salesperson": เดิมทุกรายงานดึงรายชื่อทั้งบริษัทมาให้เลือกเสมอ (ตั้งใจ ไม่ผูกกับ
        // filter.SalesEmployeeBP เพื่อให้ Leader/Manager เห็นตัวเลือกครบ) แต่พนักงานขายทั่วไป (UserRole อื่นที่ไม่ใช่
        // Leader/Manager/Admin) ข้อมูลถูกล็อกเป็นของตัวเองอยู่แล้วผ่าน ResolveEffectiveSalesEmployeeFilter จึงไม่ควร
        // เห็นชื่อพนักงานคนอื่นใน dropdown ด้วย (เทียบเท่ากับที่ AvailableBus ทำกับ BU dropdown ของ user ที่ถูกล็อก Division)
        private List<string> ResolveEffectiveSalesEmployeeOptions(List<string> options)
        {
            if (IsFullAccessUser()) return options;
            if (CurrentUserRole() == "LEADER") return options;
            if (CurrentDepartment() != "SALES") return options;   // เหตุผลเดียวกับ ResolveEffectiveSalesEmployeeFilter

            var fullName = CurrentFullName();
            return string.IsNullOrWhiteSpace(fullName) ? options : new List<string> { fullName };
        }

        // ★ เฉพาะ Sales Intelligence Matrix (9 รายงาน) — กติกาข้อ 4: Department=Sales + UserRole=Leader
        // ต้องล็อกเฉพาะ Division ตัวเองเสมอ ไม่ว่า DataScope claim จะเป็นอะไรก็ตาม เพราะ DataScope มาจาก
        // UserPermissionDto ซึ่งเป็นระบบสิทธิ์เดิมของหน้า Overview (DB1-4) คนละชุดกับ Department/UserRole นี้
        // ค่าของ Leader คนหนึ่งอาจถูกตั้ง DataScope=COMPANY ไว้เพื่อจุดประสงค์อื่นได้ จึงบังคับทับตรงนี้แทน
        private string? ResolveEffectiveBuForMatrix(string? requestedBu)
        {
            if (IsFullAccessUser()) return ResolveEffectiveBu(requestedBu);
            if (CurrentDepartment() == "SALES" && CurrentUserRole() == "LEADER") return CurrentDivision();
            return ResolveEffectiveBu(requestedBu);
        }

        // GET api/dashboard/salesoverview?Year=2026&...
        [HttpGet("salesoverview")]
        public async Task<ActionResult<SalesOverviewDto>> GetSalesOverview([FromQuery] SalesOverviewFilter filter, CancellationToken ct)
        {
            filter.Bu = ResolveEffectiveBu(filter.Bu);   // BU security (จาก JWT) — endpoint นี้ขาดบรรทัดนี้ไปแต่แรก ทำให้ Division-locked user เห็นทุก BU
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
            var result = await _salesOverviewService.GetOverviewAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/yearlycomparison  (6 ปี ปีปัจจุบันย้อนหลัง 5 ปี, รายเดือน)
        [HttpGet("yearlycomparison")]
        public async Task<ActionResult<YearlyComparisonDto>> GetYearlyComparison(CancellationToken ct)
        {
            var salesGroup = ResolveEffectiveBu(null);   // BU security (จาก JWT) — หน้านี้ไม่มี filter ให้ client เลือกเอง ต้องล็อกจาก server เท่านั้น
            var salesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(null);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
            var result = await _yearlyComparisonService.GetAsync(salesGroup, salesEmployeeBP, ct);
            return Ok(result);
        }

        // GET api/dashboard/billingdaily?DateFrom=...&DateTo=...&CustomerFullName=...&SalesEmployeeBP=...
        [HttpGet("billingdaily")]
        public async Task<ActionResult<BillingDailyDto>> GetBillingDaily([FromQuery] BillingDailyFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
            var result = await _billingDailyService.GetAsync(filter, ct);
            result.SalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.SalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/customeroverview?Year=2026&CustomerFullName=...&IndustryName=...
        [HttpGet("customeroverview")]
        public async Task<ActionResult<CustomerOverviewDto>> GetCustomerOverview([FromQuery] CustomerOverviewFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
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
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
            var result = await _productMovementService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/productoverview?Year=2026&MaterialGroupName=...
        [HttpGet("productoverview")]
        public async Task<ActionResult<ProductOverviewDto>> GetProductOverview([FromQuery] ProductOverviewFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
            var result = await _productOverviewService.GetAsync(filter, ct);
            return Ok(result);
        }

        // GET api/dashboard/saleperformance?Year=2026&SalesEmployeeBP=...
        [HttpGet("saleperformance")]
        public async Task<ActionResult<SalePerformanceDto>> GetSalePerformance([FromQuery] SalePerformanceFilter filter, CancellationToken ct)
        {
            filter.SalesGroup = ResolveEffectiveBu(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT) — UserRole ทั่วไปเห็นเฉพาะข้อมูลตัวเอง
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
            if (!CanAccessReport("PricingMarginPerformance")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _pricingMarginPerformanceService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/customerchurnanalysis?Year=2026
        [HttpGet("customerchurnanalysis")]
        public async Task<ActionResult<CustomerChurnAnalysisDto>> GetCustomerChurnAnalysis([FromQuery] CustomerChurnAnalysisFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("CustomerChurnAnalysis")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _customerChurnAnalysisService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/opportunitywinrate?DateFrom=2026-01-01&DateTo=2026-05-31
        [HttpGet("opportunitywinrate")]
        public async Task<ActionResult<OpportunityWinRateDto>> GetOpportunityWinRate([FromQuery] OpportunityWinRateFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("OpportunityWinRate")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _opportunityWinRateService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/averagedaystoclose?DateFrom=2026-01-01&DateTo=2026-05-31
        [HttpGet("averagedaystoclose")]
        public async Task<ActionResult<AverageDaysToCloseDto>> GetAverageDaysToClose([FromQuery] AverageDaysToCloseFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("AverageDaysToClose")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _averageDaysToCloseService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/crosssellupsellgains?DateFrom=2024-01-01&DateTo=2024-05-31
        [HttpGet("crosssellupsellgains")]
        public async Task<ActionResult<CrossSellUpsellGainsDto>> GetCrossSellUpsellGains([FromQuery] CrossSellUpsellGainsFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("CrossSellUpsellGains")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _crossSellUpsellGainsService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/salesforecastaccuracy?DateFrom=2024-06-01&DateTo=2024-05-31
        [HttpGet("salesforecastaccuracy")]
        public async Task<ActionResult<SalesForecastAccuracyDto>> GetSalesForecastAccuracy([FromQuery] SalesForecastAccuracyFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("SalesForecastAccuracy")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _salesForecastAccuracyService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/visitdailyreport?DateFrom=2026-08-01&DateTo=2026-08-31
        [HttpGet("visitdailyreport")]
        public async Task<ActionResult<VisitDailyReportDto>> GetVisitDailyReport([FromQuery] VisitDailyReportFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("VisitDailyReport")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _visitDailyReportService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/salesproductivity?DateFrom=2026-01-01&DateTo=2026-09-30
        [HttpGet("salesproductivity")]
        public async Task<ActionResult<SalesProductivityDto>> GetSalesProductivity([FromQuery] SalesProductivityFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("SalesProductivity")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _salesProductivityService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }

        // GET api/dashboard/forecastmonthly?FromMonth=2026-05-01&ToMonth=2026-12-01
        [HttpGet("forecastmonthly")]
        public async Task<ActionResult<ForecastMonthlyDto>> GetForecastMonthly([FromQuery] ForecastMonthlyFilter filter, CancellationToken ct)
        {
            if (!CanAccessReport("ForecastMonthly")) return Forbid();
            filter.SalesGroup = ResolveEffectiveBuForMatrix(filter.SalesGroup);   // BU security (จาก JWT)
            filter.SalesEmployeeBP = ResolveEffectiveSalesEmployeeFilter(filter.SalesEmployeeBP);   // Department/Role security (จาก JWT)
            var result = await _forecastMonthlyReportService.GetAsync(filter, ct);
            result.AvailableSalesEmployees = ResolveEffectiveSalesEmployeeOptions(result.AvailableSalesEmployees);
            return Ok(result);
        }
    }
}
