using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Services;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Runtime;
using System.Security.Claims;
using System.Text.Json;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace Mgt.Lit.WebApi.Controllers.SalesOrder
{
    [ServiceFilter(typeof(ActivityLogFilter))]
    [ApiController]
    [Route("api/MGT_SalesOrder")]
    public class SalesOrderController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SapService _sapService;


        public SalesOrderController(AppDbContext context, SapService sapService)
        {
            _context = context;
            _sapService = sapService;
        }
        [Authorize]
        [HttpPost("Saleslist")]
        public async Task<IActionResult> GetSalesOrderList([FromBody] SalesOrderRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            Console.WriteLine($"Username: {permission.Username}");
            Console.WriteLine($"CompanyID: {permission.CompanyID}");
            Console.WriteLine($"Division: {permission.Division}");
            Console.WriteLine($"DataScope(raw): {permission.DataScope}");
            Console.WriteLine($"DataScope(normalized): {DataScopes.Normalize(permission.DataScope)}");

            var query = BuildSalesBaseQuery(request, permission);
            query = ApplySalesPermission(query, permission);

            Console.WriteLine($"FullName: {permission.FullName}");
            Console.WriteLine($"SalesOrganizationCode: {permission.SalesOrganizationCode}");

            var distinctQuery = BuildDistinctSalesOrderQuery(query);

            var totalCount = await distinctQuery.CountAsync();

            var items = await distinctQuery
            .OrderByDescending(x => x.BillingDocumentDate)
            .ThenBy(x => x.BillingDocument)
            .ThenBy(x => x.SalesOrderDocument)
            .ThenBy(x => x.Material)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new SalesOrderResponseDto        // ✅ เพิ่มตรงนี้
            {
                BillingDocument = x.BillingDocument,
                BillingDocumentDate = x.BillingDocumentDate,
                SalesOrderDocument = x.SalesOrderDocument,
                SoldToParty = x.SoldToParty,
                SoldToName = x.SoldToName,
                SoldToMappingAddress = x.SoldToMappingAddress,  // ✅ ที่อยู่ Sold-to
                ShiptoCode = x.ShiptoCode,
                ShipToName = x.ShipToName,
                ShipToMappingAddress = x.ShipToMappingAddress,  // ✅ ที่อยู่ Ship-to
                Material = x.Material,
                MaterialName = x.MaterialName,
                SalesEmployee = x.SalesEmployee
            })
            .ToListAsync();

            return Ok(new
            {
                totalCount,
                page = request.Page,
                pageSize = request.PageSize,
                items
            });
        }
        private IQueryable<View_MGT_GLC_ALL_Sales> BuildSalesBaseQuery(
    SalesOrderRequestDto request,
    View_UserPermission permission)
        {
            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.BillingDocument != null);

            if (!string.IsNullOrEmpty(request.SalesOrganization))
                query = query.Where(x => x.SalesOrganization == request.SalesOrganization);

            if (request.DocDateFrom.HasValue)
            {
                var from = request.DocDateFrom.Value.Date;
                query = query.Where(x => x.BillingDocumentDate >= from);
            }

            if (request.DocDateTo.HasValue)
            {
                var to = request.DocDateTo.Value.Date.AddDays(1);
                query = query.Where(x => x.BillingDocumentDate < to);
            }

            if (!string.IsNullOrWhiteSpace(request.BillingDocument))
            {
                var keyword = $"%{request.BillingDocument.Trim()}%";
                query = query.Where(x =>
                    EF.Functions.Like(x.BillingDocument, keyword) ||
                    EF.Functions.Like(x.SalesDocument, keyword));
            }

            if (!string.IsNullOrWhiteSpace(request.SoldToParty))
            {
                var keyword = $"%{request.SoldToParty.Trim()}%";
                query = query.Where(x =>
                    EF.Functions.Like(x.SoldToParty, keyword) ||
                    EF.Functions.Like(x.SoldToName, keyword));
            }

            if (!string.IsNullOrWhiteSpace(request.Material))
            {
                var keyword = $"%{request.Material.Trim()}%";
                query = query.Where(x =>
                    EF.Functions.Like(x.Material, keyword) ||
                    EF.Functions.Like(x.MaterialName, keyword));
            }

            return query;
        }
        [Authorize]
        [HttpPost("ExportSales")]
        public async Task<IActionResult> ExportSales([FromBody] SalesOrderRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            var query = BuildSalesBaseQuery(request, permission);
            query = ApplySalesPermission(query, permission);

            var items = await BuildDistinctSalesOrderQuery(query)
                .OrderByDescending(x => x.BillingDocumentDate)
                .ThenBy(x => x.BillingDocument)
                .ThenBy(x => x.SalesOrderDocument)
                .ThenBy(x => x.Material)
                .Select(x => new
                {
                    x.BillingDocument,
                    x.BillingDocumentDate,
                    x.SalesOrderDocument,
                    x.SoldToParty,
                    x.SoldToName,
                    x.SoldToMappingAddress,
                    x.ShiptoCode,
                    x.ShipToName,
                    x.ShipToMappingAddress,
                    x.Material,
                    x.MaterialName,
                    x.SalesEmployee
                })
                .ToListAsync();

            return Ok(items);
        }
        [Authorize]
        [HttpPost("StockofMaterial")]
        public async Task<IActionResult> GetStockofMaterial([FromBody] MaterialStockRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null) return Unauthorized();
            if (!permission.Page2Access) return Forbid();

            // ✅ DataScope กรอง Material
            var material = request.Material?.Trim();
            var allowedMaterials = await GetAllowedMaterialsAsync(permission);
            if (allowedMaterials != null &&
                !string.IsNullOrWhiteSpace(material) &&
                !allowedMaterials.Contains(material))
                return Forbid();

            var sqlItems = await _context.View_MaterialStock_WeightKG
                .AsNoTracking()
                .Where(x =>
                    (string.IsNullOrEmpty(request.Plant) || x.Plant == request.Plant) &&
                    (string.IsNullOrEmpty(material) || x.Material == material))
                .ToListAsync();

            var sqlDict = sqlItems
                .GroupBy(x => x.Material!)
                .ToDictionary(g => g.Key, g => g.First());

            var sapJson = await _sapService
                .GetWarehouseStockCostByBatchAsync(request.Material, request.Batch);

            var sapResponse = JsonSerializer.Deserialize<SapWarehouseStockResponseDto>(
                sapJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var sapData = sapResponse?._Detail ?? new List<SapWarehouseStockDetailDto>();

            var salesOrg = GetCompanySalesOrg(permission.CompanyID);

            var materialGroups = await _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.Material != null && x.SalesGroup != null)
                .Where(x => string.IsNullOrWhiteSpace(salesOrg) || x.SalesOrganization == salesOrg)
                .Select(x => new
                {
                    x.Material,
                    x.MaterialGroupName,
                    x.SalesGroup,
                    x.IndustryName,
                    x.SalesEmployeeID
                })
                .ToListAsync();

            var materialGroupDict = materialGroups
                .GroupBy(x => x.Material!)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        MaterialGroupName = g.First().MaterialGroupName ?? "",
                        SalesGroup = string.Join(", ",
                            g.Where(x => !string.IsNullOrWhiteSpace(x.SalesGroup))
                             .Select(x => x.SalesGroup)
                             .Distinct())
                    });

            var items = sapData
                .Select(s =>
                {
                    sqlDict.TryGetValue(s.Material_Code ?? "", out var sql);
                    materialGroupDict.TryGetValue(s.Material_Code ?? "", out var matInfo);

                    decimal unitKg = 0;
                    if (sql?.NetWeight != null && sql.NetWeight > 0)
                        unitKg = s.Price_KG / sql.NetWeight.Value;

                    return new MaterialStockResponseDto
                    {
                        MaterialCode = s.Material_Code,
                        MaterialDescription = s.Material_Name,
                        BatchNo = s.Batch_Code?.Trim().TrimEnd('.'),
                        Plant = s.Plant,
                        StorageLocation = "-",
                        Unrestricted_Stock = s.Unrestricted_Stock,
                        QualityInspection = s.Stock_in_QI,

                        // ✅ CanViewCost
                        TotalValue = permission.CanViewCost
                            ? s.Value_of_Unrestricted_Stock + s.Value_of_Blocked_Stock + s.Value_of_Stock_in_QI
                            : 0,
                        CostPerKg = permission.CanViewCost ? s.Price_KG : 0,

                        ExpDate = s.EXP_Date,
                        Unit = sql?.Unit ?? s.Unit,
                        MaterialGroup = matInfo?.MaterialGroupName,
                        SalesGroup = matInfo?.SalesGroup,
                        ConversionText = sql?.NetWeight?.ToString("G29") ?? "0",
                        UnitKg = unitKg
                    };
                })
                .GroupBy(x => new { x.BatchNo, x.Unrestricted_Stock })
                .Select(g => g.First())
                .OrderBy(x => x.BatchNo)
                .ToList();

            return Ok(new
            {
                totalCount = items.Count,
                page = request.Page,
                pageSize = request.PageSize,
                items
            });
        }
        //Api MD04 Warehouse Stock Movement
        [Authorize]
        [HttpPost("StockMovement")]
        public async Task<IActionResult> GetStockRequirementSap([FromBody] StockMovementRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null) return Unauthorized();
            if (!permission.Page3Access) return Forbid();

            var material = request.Material?.Trim();
            var requestedPlant = request.Plant?.Trim();

            if (string.IsNullOrWhiteSpace(material))
                return BadRequest(new { message = "Material is required." });

            var allowedMaterials = await GetAllowedMaterialsAsync(permission);
            if (allowedMaterials != null && !allowedMaterials.Contains(material))
                return Forbid();

            var plantsToQuery = GetPlantsForStockMovement(permission, requestedPlant).ToList();
            if (!plantsToQuery.Any()) return Forbid();

            try
            {
                StockRequirementResponse? mergedSapData = null;

                foreach (var plant in plantsToQuery)
                {
                    var jsonResult = await _sapService.GetStockRequirementAsync(material, plant);
                    var currentSapData = JsonSerializer.Deserialize<StockRequirementResponse>(
                        jsonResult,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (currentSapData == null) continue;

                    if (mergedSapData == null)
                        mergedSapData = currentSapData;
                    else if (currentSapData._Detail != null && currentSapData._Detail.Any())
                    {
                        if (mergedSapData._Detail == null)
                            mergedSapData._Detail = currentSapData._Detail;
                        else
                            mergedSapData._Detail.AddRange(currentSapData._Detail);
                    }
                }

                if (mergedSapData == null || mergedSapData._Detail == null || !mergedSapData._Detail.Any())
                    return Ok(new
                    {
                        material_Code = material,
                        plant = requestedPlant ?? string.Join(",", plantsToQuery),
                        _Detail = new List<object>()
                    });

                // ── Material Group ────────────────────────────────────────────────────
                var materialCodes = mergedSapData._Detail
                    .Select(x => x.Material_Code)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

                if (materialCodes.Any())
                {
                    var salesOrg = GetCompanySalesOrg(permission.CompanyID);
                    var materialQuery = _context.View_MGT_GLC_ALL_Sales
                        .AsNoTracking()
                        .Where(x => materialCodes.Contains(x.Material));

                    if (ResolveScope(permission) != DataScopes.CrossCompany &&
                        !string.IsNullOrWhiteSpace(salesOrg))
                        materialQuery = materialQuery.Where(x => x.SalesOrganization == salesOrg);

                    var materialInfo = await materialQuery
                        .Select(x => new { x.Material, x.MaterialGroup1, x.MaterialGroupName, x.SalesGroup })
                        .Distinct()
                        .ToListAsync();

                    var materialDict = materialInfo
                        .GroupBy(x => x.Material)
                        .ToDictionary(g => g.Key, g => new
                        {
                            g.First().MaterialGroup1,
                            g.First().MaterialGroupName,
                            SalesGroup = string.Join(", ",
                                g.Where(x => !string.IsNullOrWhiteSpace(x.SalesGroup))
                                 .Select(x => x.SalesGroup).Distinct())
                        });

                    foreach (var item in mergedSapData._Detail)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Material_Code) &&
                            materialDict.TryGetValue(item.Material_Code, out var info))
                        {
                            item.MaterialGroup = info.MaterialGroup1;
                            item.MaterialGroupDescription = info.MaterialGroupName;
                            item.SalesGroup = info.SalesGroup;
                        }
                    }
                }

                // ── ✅ Clear ค่าจาก SAP ก่อนทุกครั้ง ─────────────────────────────────
                foreach (var item in mergedSapData._Detail)
                {
                    item.Customer_Code = null;
                    item.Customer_Name = null;
                    item.Additional_Info_Out = null;
                }

                // ── เทียบ RefDoc กับ OpSalesOrder + SalesEmployee ─────────────────────
                var refDocs = mergedSapData._Detail
                    .Where(x => !string.IsNullOrWhiteSpace(x.Ref_Doc))
                    .Select(x => x.Ref_Doc!.Trim())
                    .Distinct()
                    .ToList();

                var currentScope = ResolveScope(permission);

                // ✅ ดึง SalesDocument ที่ user นี้มีสิทธิ์จริงๆ
                HashSet<string> ownRefDocs = new(StringComparer.OrdinalIgnoreCase);

                if (refDocs.Any() &&
                    currentScope != DataScopes.CrossCompany &&
                    currentScope != DataScopes.Company &&
                    currentScope != DataScopes.Division)
                {
                    // ✅ วิธีที่ 1: เทียบจาก View_MGT_GLC_ALL_Sales (SalesEmployeeID)
                    var ownDocQuery = _context.View_MGT_GLC_ALL_Sales
                        .AsNoTracking()
                        .Where(x => x.SalesDocument != null && refDocs.Contains(x.SalesDocument));

                    ownDocQuery = ApplySalesPermission(ownDocQuery, permission);

                    ownRefDocs = (await ownDocQuery
                        .Select(x => x.SalesDocument!)
                        .Distinct()
                        .ToListAsync())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // ✅ วิธีที่ 2: fallback เทียบ MGT Code ของ user จาก View_Sales_with_Op_SalesOrder
                    var userMgtCode = permission.Username?.Trim();

                    if (!string.IsNullOrWhiteSpace(userMgtCode))
                    {
                        var ownOpDocs = await _context.View_Sales_with_Op_SalesOrder
                            .AsNoTracking()
                            .Where(x => x.PartnerFunction == "Z2" &&
                                        x.Customer != null &&
                                        x.Customer.Trim().ToUpper() == userMgtCode.ToUpper() &&
                                        x.SalesOrder != null &&
                                        refDocs.Contains(x.SalesOrder))
                            .Select(x => x.SalesOrder!)
                            .Distinct()
                            .ToListAsync();

                        // ✅ รวม 2 วิธีเข้าด้วยกัน
                        foreach (var doc in ownOpDocs)
                            ownRefDocs.Add(doc);
                    }
                }

                if (refDocs.Any())
                {
                    var opSalesData = await _context.View_Sales_with_Op_SalesOrder
                        .AsNoTracking()
                        .Where(x => x.PartnerFunction == "Z2" &&
                                    x.Customer != null &&
                                   (EF.Functions.Like(x.Customer, "MGT%") ||
                                    EF.Functions.Like(x.Customer, "GLC%")) &&
                                    x.SalesOrder != null &&
                                    refDocs.Contains(x.SalesOrder))
                        .Select(x => new { x.SalesOrder, x.Customer, x.SoldToParty })
                        .ToListAsync();

                    var opSalesLookup = opSalesData
                        .GroupBy(x => x.SalesOrder!)
                        .ToDictionary(g => g.Key, g => g.First());

                    var salesData = await _context.View_MGT_GLC_ALL_Sales
                        .AsNoTracking()
                        .Where(x => x.SalesDocument != null && refDocs.Contains(x.SalesDocument))
                        .Select(x => new { x.SalesDocument, x.SalesEmployeeID, x.SoldToName })
                        .Distinct()
                        .ToListAsync();

                    var salesLookup = salesData
                        .GroupBy(x => x.SalesDocument!)
                        .ToDictionary(g => g.Key, g => g.First());

                    foreach (var item in mergedSapData._Detail)
                    {
                        var refDoc = item.Ref_Doc?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(refDoc)) continue;

                        bool canView = currentScope == DataScopes.CrossCompany ||
                                       currentScope == DataScopes.Company ||
                                       currentScope == DataScopes.Division ||
                                       ownRefDocs.Contains(refDoc);

                        if (opSalesLookup.TryGetValue(refDoc, out var opSales) && canView)
                        {
                            item.Customer_Code = opSales.Customer;
                            item.Customer_Name = opSales.SoldToParty;
                        }

                        if (salesLookup.TryGetValue(refDoc, out var sales) && canView)
                        {
                            item.Additional_Info_Out = string.Join(" | ",
                                new[] { sales.SalesEmployeeID, sales.SoldToName }
                                .Where(x => !string.IsNullOrWhiteSpace(x)));
                        }
                        // ✅ fallback Additional_Info_Out จาก opSales ถ้า salesLookup ไม่มี
                        else if (opSalesLookup.TryGetValue(refDoc, out var opSalesFallback) && canView
                                 && string.IsNullOrWhiteSpace(item.Additional_Info_Out))
                        {
                            item.Additional_Info_Out = opSalesFallback.SoldToParty;
                        }
                    }
                }

                // ── CanViewVendor ─────────────────────────────────────────────────────
                if (!permission.CanViewVendor)
                    foreach (var item in mergedSapData._Detail)
                    {
                        item.Vendor_Code = null;
                        item.Vendor_Name = null;
                    }

                // ── CanViewCustomer ───────────────────────────────────────────────────
                if (!permission.CanViewCustomer)
                    foreach (var item in mergedSapData._Detail)
                    {
                        item.Customer_Code = null;
                        item.Customer_Name = null;
                    }

                mergedSapData.Plant = requestedPlant ?? string.Join(",", plantsToQuery);
                return Ok(mergedSapData);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        //Api MB52 Warehouse Stock Movement
        [Authorize]
        [HttpPost("Stockmaterial")]
        public async Task<IActionResult> GetStockMaterialSap([FromBody] MaterialStockRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page2Access)
                return Forbid();

            try
            {
                var jsonResult = await _sapService
                    .GetWarehouseStockCostByBatchAsync(request.Material_Code, request.Batch_Code);

                return Content(jsonResult, "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [Authorize]
        [HttpPost("MaterialConsumption")]
        public async Task<IActionResult> GetMaterialConsumption([FromBody] MaterialConsumptionRequest request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page3Access)
                return Forbid();

            if (string.IsNullOrWhiteSpace(request.Material))
                return BadRequest("Material is required");

            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.Material == request.Material);

            query = ApplySalesPermission(query, permission);

            var data = await query
                .GroupBy(x => new
                {
                    x.Material,
                    x.MaterialName,
                    x.x_Year
                })
                .Select(g => new
                {
                    Material = g.Key.Material,
                    MaterialName = g.Key.MaterialName,
                    Year = g.Key.x_Year,

                    January = g.Sum(x => x.x_Month == 1 ? x.Quantity : 0),
                    February = g.Sum(x => x.x_Month == 2 ? x.Quantity : 0),
                    March = g.Sum(x => x.x_Month == 3 ? x.Quantity : 0),
                    April = g.Sum(x => x.x_Month == 4 ? x.Quantity : 0),
                    May = g.Sum(x => x.x_Month == 5 ? x.Quantity : 0),
                    June = g.Sum(x => x.x_Month == 6 ? x.Quantity : 0),
                    July = g.Sum(x => x.x_Month == 7 ? x.Quantity : 0),
                    August = g.Sum(x => x.x_Month == 8 ? x.Quantity : 0),
                    September = g.Sum(x => x.x_Month == 9 ? x.Quantity : 0),
                    October = g.Sum(x => x.x_Month == 10 ? x.Quantity : 0),
                    November = g.Sum(x => x.x_Month == 11 ? x.Quantity : 0),
                    December = g.Sum(x => x.x_Month == 12 ? x.Quantity : 0),
                    Total = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.Year)
                .ToListAsync();

            // ✅ ดึง NetWeight ของ Material นี้
            var netWeight = await _context.View_MaterialStock_WeightKG
                .AsNoTracking()
                .Where(x => x.Material == request.Material)
                .Select(x => x.NetWeight)
                .FirstOrDefaultAsync();

            // ✅ Map เพิ่ม KG
            var result = data.Select(x => new
            {
                x.Material,
                x.MaterialName,
                x.Year,

                x.January,
                JanuaryKG = netWeight.HasValue ? x.January * netWeight : null,
                x.February,
                FebruaryKG = netWeight.HasValue ? x.February * netWeight : null,
                x.March,
                MarchKG = netWeight.HasValue ? x.March * netWeight : null,
                x.April,
                AprilKG = netWeight.HasValue ? x.April * netWeight : null,
                x.May,
                MayKG = netWeight.HasValue ? x.May * netWeight : null,
                x.June,
                JuneKG = netWeight.HasValue ? x.June * netWeight : null,
                x.July,
                JulyKG = netWeight.HasValue ? x.July * netWeight : null,
                x.August,
                AugustKG = netWeight.HasValue ? x.August * netWeight : null,
                x.September,
                SeptemberKG = netWeight.HasValue ? x.September * netWeight : null,
                x.October,
                OctoberKG = netWeight.HasValue ? x.October * netWeight : null,
                x.November,
                NovemberKG = netWeight.HasValue ? x.November * netWeight : null,
                x.December,
                DecemberKG = netWeight.HasValue ? x.December * netWeight : null,
                x.Total,
                TotalKG = netWeight.HasValue ? x.Total * netWeight : null,

                NetWeight = netWeight  // ✅ ส่ง NetWeight กลับไปด้วยให้ frontend รู้
            });

            return Ok(result);
        }
        [Authorize]
        [HttpGet("MaterialLookup")]
        public async Task<IActionResult> GetMaterialLookup([FromQuery] string keyword)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page3Access && !permission.Page2Access)
                return Forbid();

            keyword = keyword?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(keyword))
                return Ok(new List<object>());

            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.Material != null);

            query = ApplySalesPermission(query, permission);
            // เป็น
            if (keyword.Length < 2)   // ← ลดจาก 3 เป็น 2
                return Ok(new List<object>());

            var likeStart = $"{keyword}%";   // starts-with
            var likeAny = $"%{keyword}%";  // contains
            _context.Database.SetCommandTimeout(60);

            var items = await query
                .Where(x =>
                    EF.Functions.Like(x.Material, likeAny) ||
                    EF.Functions.Like(x.MaterialName, likeAny))
                .Select(x => new
                {
                    Code = x.Material,
                    Name = x.MaterialName,
                    // ✅ priority: 0 = code starts-with, 1 = name starts-with, 2 = contains only
                    Priority = EF.Functions.Like(x.Material, likeStart) ? 0
                             : EF.Functions.Like(x.MaterialName, likeStart) ? 1
                             : 2
                })
                .Distinct()
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Take(20)
                .Select(x => new { x.Code, x.Name })   // strip priority ออกก่อน return
                .ToListAsync();

            return Ok(items);
        }
        [Authorize]
        [HttpPost("nofreport")]
        public async Task<IActionResult> GetNofReport([FromBody] NofReportRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            // ── Base query ────────────────────────────────────────────────────────────
            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.ProductGroup == "NOF");

            if (!string.IsNullOrWhiteSpace(request.Material))
            {
                var keyword = $"%{request.Material.Trim()}%";
                query = query.Where(x =>
                    EF.Functions.Like(x.Material, keyword) ||
                    EF.Functions.Like(x.MaterialName, keyword));
            }

            if (request.DateFrom.HasValue)
            {
                var from = request.DateFrom.Value.Date;
                query = query.Where(x => x.BillingDocumentDate >= from);
            }

            if (request.DateTo.HasValue)
            {
                var to = request.DateTo.Value.Date.AddDays(1);
                query = query.Where(x => x.BillingDocumentDate < to);
            }

            query = ApplySalesPermission(query, permission);

            // ── Step 1: count + availableMonths (ก่อน pagination) ────────────────────
            var totalCount = await query.CountAsync();

            var rawDates = await query
                .Where(x => x.BillingDocumentDate != null)
                .Select(x => x.BillingDocumentDate!.Value)
                .Distinct()
                .ToListAsync();

            var availableMonths = rawDates
                .Select(d => d.ToString("yyyy/MM"))
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            // ── Step 2: salesItems (หลัง pagination) ──────────────────────────────────
            var salesItems = await query
                .OrderByDescending(x => x.BillingDocumentDate)
                .ThenBy(x => x.BillingDocument)
                .ThenBy(x => x.Material)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(x => new
                {
                    x.BillingDocument,
                    x.BillingDocumentDate,
                    x.SoldToParty,
                    x.SoldToName,
                    x.Material,
                    x.MaterialName,
                    x.ProductGroup,
                    x.Quantity,
                    x.Unit,
                    x.NetAmount,
                    x.CostAmount,
                    x.GrossProfit,
                    x.SalesEmployeeID
                })
                .ToListAsync();

            // ── Step 3: NetWeight ──────────────────────────────────────────────────────
            var materialCodes = salesItems
                .Where(x => x.Material != null)
                .Select(x => x.Material!)
                .Distinct()
                .ToList();

            var weightDict = new Dictionary<string, decimal?>();

            if (materialCodes.Any())
            {
                var weights = await _context.View_MaterialStock_WeightKG
                    .AsNoTracking()
                    .Where(x => x.Material != null && materialCodes.Contains(x.Material))
                    .Select(x => new { x.Material, x.NetWeight })
                    .ToListAsync();

                weightDict = weights
                    .GroupBy(x => x.Material!)
                    .ToDictionary(g => g.Key, g => g.First().NetWeight);
            }

            // ── Step 4: map ───────────────────────────────────────────────────────────
            var items = salesItems.Select(x =>
            {
                weightDict.TryGetValue(x.Material ?? "", out var netWeight);

                return new NofReportResponseDto
                {
                    BillingDocument = x.BillingDocument,
                    BillingDocumentDate = x.BillingDocumentDate,
                    SoldToParty = x.SoldToParty,
                    SoldToName = x.SoldToName,
                    Material = x.Material,
                    MaterialName = x.MaterialName,
                    ProductGroup = x.ProductGroup,
                    Quantity = x.Quantity,
                    Unit = x.Unit,
                    NetAmount = x.NetAmount,
                    CostAmount = x.CostAmount,
                    GrossProfit = x.GrossProfit,
                    SalesEmployee = x.SalesEmployeeID,
                    NetWeight = netWeight,
                    QuantityKG = (x.Quantity.HasValue && netWeight.HasValue)
                        ? x.Quantity.Value * netWeight.Value
                        : (decimal?)null
                };
            }).ToList();

            // ── Response ──────────────────────────────────────────────────────────────
            return Ok(new
            {
                totalCount,
                page = request.Page,
                pageSize = request.PageSize,
                availableMonths,
                items
            });
        }
        [Authorize]
        [HttpPost("OpSalesOrder")]
        public async Task<IActionResult> GetOpSalesOrder([FromBody] OpSalesOrderRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            var query = _context.View_Sales_with_Op_SalesOrder
                .AsNoTracking()
                .Where(x => x.Customer != null &&
                           (EF.Functions.Like(x.Customer, "MGT%") ||
                            EF.Functions.Like(x.Customer, "GLC%")));

            // ✅ Filter PartnerFunction = Z2 เฉพาะ MGT/GLC
            query = query.Where(x => x.PartnerFunction == "Z2");

            if (!string.IsNullOrWhiteSpace(request.SalesOrder))
                query = query.Where(x => x.SalesOrder == request.SalesOrder.Trim());

            if (!string.IsNullOrWhiteSpace(request.SoldToParty))
                query = query.Where(x => x.SoldToParty == request.SoldToParty.Trim());

            if (request.DateFrom.HasValue)
                query = query.Where(x => x.SalesOrderDate >= request.DateFrom.Value.Date);

            if (request.DateTo.HasValue)
                query = query.Where(x => x.SalesOrderDate < request.DateTo.Value.Date.AddDays(1));

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.SalesOrderDate)
                .ThenBy(x => x.SalesOrder)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(x => new
                {
                    x.SalesOrder,
                    x.System_Name,
                    x.SalesOrderDate,
                    x.SalesOrderType,
                    x.SalesOrganization,
                    x.DistributionChannel,
                    x.SalesGroup,
                    x.SoldToParty,
                    x.PurchaseOrderByCustomer,
                    x.TotolNetAmont,
                    x.OverallDeliveryStatus,
                    x.RequestedDeliveryDate,
                    x.CustomerPaymentTerms,
                    x.BillingDoucumentDate,
                    x.PartnerFunction,
                    x.Customer,
                    x.AddressID
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                page = request.Page,
                pageSize = request.PageSize,
                items
            });
        }
        [Authorize]
        [HttpGet("SoldToLookup")]
        public async Task<IActionResult> GetSoldToLookup([FromQuery] string keyword)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            keyword = keyword?.Trim() ?? "";

            if (keyword.Length < 2)
                return Ok(new List<object>());

            var likeStart = $"{keyword}%";
            var likeAny = $"%{keyword}%";

            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.SoldToParty != null);

            query = ApplySalesPermission(query, permission);

            _context.Database.SetCommandTimeout(60);

            var items = await query
                .Where(x =>
                    EF.Functions.Like(x.SoldToParty, likeAny) ||
                    EF.Functions.Like(x.SoldToName, likeAny))
                .Select(x => new
                {
                    Code = x.SoldToParty,
                    Name = x.SoldToName,
                    Priority = EF.Functions.Like(x.SoldToParty, likeStart) ? 0  // code ขึ้นต้นตรง
                             : EF.Functions.Like(x.SoldToName, likeStart) ? 1  // name ขึ้นต้นตรง
                             : 2                                                  // contains เฉยๆ
                })
                .Distinct()
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Take(20)
                .Select(x => new { x.Code, x.Name })
                .ToListAsync();

            return Ok(items);
        }
        private IQueryable<View_MGT_GLC_ALL_Sales> ApplySalesPermission(
     IQueryable<View_MGT_GLC_ALL_Sales> query,
     View_UserPermission permission)
        {
            var scope = ResolveScope(permission);
            var salesOrg = NormalizeKey(permission.SalesOrganizationCode);

            if (scope != DataScopes.CrossCompany)
            {
                if (string.IsNullOrWhiteSpace(salesOrg))
                    return query.Where(x => false);

                query = query.Where(x =>
                    x.SalesOrganization != null &&
                    x.SalesOrganization.Trim().ToUpper() == salesOrg);
            }

            return scope switch
            {
                DataScopes.CrossCompany => query,
                DataScopes.Company => query,
                DataScopes.Division => ApplyDivisionScope(query, permission),
                _ => ApplyOwnScope(query, permission)
            };
        }

        private string ResolveScope(View_UserPermission permission)
        {
            var rawScope = permission.DataScope?.Trim();

            // ใช้ DataScope จาก View_UserPermission ก่อน
            // จะรองรับ Ms_UserPermissionOverride ได้ทันที
            if (!string.IsNullOrWhiteSpace(rawScope))
                return DataScopes.Normalize(rawScope);

            var tier = (permission.Tier ?? "").Trim().ToUpperInvariant();
            var roleKey = (permission.RoleKey ?? "").Trim().ToUpperInvariant();
            var userRole = (permission.UserRole ?? "").Trim().ToUpperInvariant();

            if (tier == "TIER4" && (roleKey == "ADMIN" || userRole == "ADMIN"))
                return DataScopes.CrossCompany;

            if (tier == "TIER3" && (roleKey == "MANAGER" || userRole == "MANAGER"))
                return DataScopes.Company;

            if (tier == "TIER2" && (roleKey == "LEADER" || userRole == "LEADER"))
                return DataScopes.Division;

            if (tier == "TIER1")
                return DataScopes.Own;

            return DataScopes.Own;
        }

        private IQueryable<View_MGT_GLC_ALL_Sales> ApplyDivisionScope(
    IQueryable<View_MGT_GLC_ALL_Sales> query,
    View_UserPermission permission)
        {
            var salesOrg = NormalizeKey(permission.SalesOrganizationCode);
            var division = NormalizeKey(permission.Division);

            if (string.IsNullOrWhiteSpace(salesOrg) || string.IsNullOrWhiteSpace(division))
                return query.Where(x => false);

            // MGT = 1000 ใช้แบบเดิม
            if (salesOrg == "1000")
            {
                return query.Where(x =>
                    x.SalesGroup != null &&
                    x.SalesGroup.Trim().ToUpper() == division);
            }

            // GLC = 2000 ใช้ mapping ตามสิทธิ์ของกลุ่มคน
            if (salesOrg == "2000")
            {
                return ApplyGlcDivisionScope(query, permission, division, salesOrg);
            }

            return query.Where(x => false);
        }
        private IQueryable<View_MGT_GLC_ALL_Sales> ApplyGlcDivisionScope(
    IQueryable<View_MGT_GLC_ALL_Sales> query,
    View_UserPermission permission,
    string division,
    string salesOrg)
        {
            if (!permission.CompanyID.HasValue)
                return query.Where(x => false);

            var allowedDivisions = GetGlcLeaderDivisions(division);

            var allowedEmployeesQuery = _context.View_UserPermissions
                .AsNoTracking()
                .Where(p =>
                    p.CompanyID == permission.CompanyID.Value &&
                    p.FullName != null &&
                    p.Division != null &&
                    p.SalesOrganizationCode != null &&
                    p.SalesOrganizationCode.Trim().ToUpper() == salesOrg &&
                    allowedDivisions.Contains(p.Division.Trim().ToUpper()))
                .Select(p => p.FullName!.Trim().ToUpper())
                .Distinct();

            return query.Where(x =>
                x.SalesEmployeeID != null &&
                allowedEmployeesQuery.Contains(x.SalesEmployeeID.Trim().ToUpper()));
        }
        private static string[] GetGlcLeaderDivisions(string division)
        {
            return division switch
            {
                "FD" => new[] { "FD", "FD1" },
                "PH" => new[] { "PH1", "PH2", "PH3" },
                "SK" => new[] { "SK" },
                _ => new[] { division }
            };
        }

        private static string NormalizeKey(string? value)
        {
            return value?.Trim().ToUpperInvariant() ?? string.Empty;
        }
        private IQueryable<View_MGT_GLC_ALL_Sales> ApplyOwnScope(
    IQueryable<View_MGT_GLC_ALL_Sales> query,
    View_UserPermission permission)
        {
            var fullName = NormalizeKey(permission.FullName);
            var username = NormalizeKey(permission.Username);

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return query.Where(x =>
                    x.SalesEmployeeID != null &&
                    x.SalesEmployeeID.Trim().ToUpper() == fullName);
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                return query.Where(x =>
                    x.SalesEmployeeID != null &&
                    x.SalesEmployeeID.Trim().ToUpper() == username);
            }

            return query.Where(x => false);
        }
        private async Task<View_UserPermission?> GetCurrentPermissionAsync()
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var companyIdText = User.FindFirst("CompanyID")?.Value;

            if (string.IsNullOrWhiteSpace(username))
                return null;

            if (!int.TryParse(companyIdText, out var companyId))
                return null;

            return await _context.View_UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.CompanyID == companyId);
        }

        private static readonly Dictionary<int, HashSet<string>> CompanyPlants = new()
        {
            [1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1100", "1900" },
            [2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "2100" }
        };

        private static string? GetCompanySalesOrg(int? companyId) => companyId switch
        {
            1 => "1000",
            2 => "2000",
            _ => null
        };

        private IEnumerable<string> GetPlantsForStockMovement(
    View_UserPermission permission,
    string? requestedPlant)
        {
            var scope = ResolveScope(permission);

            var allPlants = CompanyPlants
                .Values
                .SelectMany(x => x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (scope == DataScopes.CrossCompany)
            {
                if (!string.IsNullOrWhiteSpace(requestedPlant))
                    return allPlants.Contains(requestedPlant)
                        ? new[] { requestedPlant }
                        : Enumerable.Empty<string>();

                return allPlants;
            }

            if (!permission.CompanyID.HasValue)
                return Enumerable.Empty<string>();

            if (!CompanyPlants.TryGetValue(permission.CompanyID.Value, out var companyPlants))
                return Enumerable.Empty<string>();

            if (!string.IsNullOrWhiteSpace(requestedPlant))
            {
                return companyPlants.Contains(requestedPlant)
                    ? new[] { requestedPlant }
                    : Enumerable.Empty<string>();
            }

            return companyPlants;
        }
        private IQueryable<SalesOrderResponseDto> BuildDistinctSalesOrderQuery(
    IQueryable<View_MGT_GLC_ALL_Sales> query)
        {
            return query
                .GroupBy(x => new
                {
                    x.BillingDocumentDate,
                    x.BillingDocument,
                    x.SalesDocument,
                    x.SoldToParty,
                    x.SoldToName,
                    x.SoldtoMappingAddress,
                    x.ShiptoCode,
                    x.ShipToName,
                    x.ShiptoMappingAddress,
                    x.Material
                })
                .Select(g => new SalesOrderResponseDto
                {
                    BillingDocument = g.Key.BillingDocument ?? "-",
                    BillingDocumentDate = g.Key.BillingDocumentDate ?? DateTime.MinValue,
                    SalesOrderDocument = g.Key.SalesDocument ?? "-",
                    SoldToParty = g.Key.SoldToParty ?? "-",
                    SoldToName = g.Key.SoldToName ?? "-",
                    SoldToMappingAddress = g.Key.SoldtoMappingAddress ?? "-",
                    ShiptoCode = g.Key.ShiptoCode ?? "-",
                    ShipToName = g.Key.ShipToName ?? "-",
                    ShipToMappingAddress = g.Key.ShiptoMappingAddress ?? "-",
                    Material = g.Key.Material ?? "-",

                    // ไม่เอา MaterialName เป็น key แต่เก็บมาแสดง 1 ค่า
                    MaterialName = g.Max(x => x.MaterialName) ?? "-",

                    // ไม่เอา SalesEmployee เป็น key
                    // ถ้าซ้ำกันจริง ๆ ส่วนมากค่านี้จะเหมือนกันอยู่แล้ว
                    SalesEmployee = g.Max(x => x.SalesEmployeeID) ?? "-"
                });
        }
        private async Task<HashSet<string>?> GetAllowedMaterialsAsync(View_UserPermission permission)
        {
            var scope = ResolveScope(permission);

            if (scope == DataScopes.Company || scope == DataScopes.CrossCompany || scope == DataScopes.Division)
                return null; // ไม่จำกัด

            var query = _context.View_MGT_GLC_ALL_Sales.AsNoTracking();
            query = ApplySalesPermission(query, permission);

            var materials = await query
                .Where(x => x.Material != null)
                .Select(x => x.Material!)
                .Distinct()
                .ToListAsync();

            return new HashSet<string>(materials, StringComparer.OrdinalIgnoreCase);
        }
    }

}
