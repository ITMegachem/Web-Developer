using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Services;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
            if (permission == null)
                return Unauthorized();

            if (!permission.Page2Access)
                return Forbid();

            var material = request.Material?.Trim();

            var sqlItems = await _context.View_MaterialStock_WeightKG
                .AsNoTracking()
                .Where(x =>
                    (string.IsNullOrEmpty(request.Plant) || x.Plant == request.Plant) &&
                    (string.IsNullOrEmpty(material) || x.Material == material)
                )
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

            var materialGroups = await _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.Material != null)
                .Select(x => new
                {
                    x.Material,
                    x.MaterialGroupName
                })
                .ToListAsync();

            var materialGroupDict = materialGroups
                .GroupBy(x => x.Material!)
                .ToDictionary(g => g.Key, g => g.First().MaterialGroupName ?? "");

            var items = sapData
                .Select(s =>
                {
                    sqlDict.TryGetValue(s.Material_Code ?? "", out var sql);

                    decimal unitKg = 0;
                    if (sql?.NetWeight != null && sql.NetWeight > 0)
                    {
                        unitKg = s.Price_KG / sql.NetWeight.Value;
                    }

                    return new MaterialStockResponseDto
                    {
                        MaterialCode = s.Material_Code,
                        MaterialDescription = s.Material_Name,
                        BatchNo = s.Batch_Code?.Trim().TrimEnd('.'),
                        Plant = s.Plant,
                        StorageLocation = "-",
                        Unrestricted_Stock = s.Unrestricted_Stock,
                        QualityInspection = s.Stock_in_QI,

                        TotalValue = permission.CanViewCost
                            ? s.Value_of_Unrestricted_Stock + s.Value_of_Blocked_Stock + s.Value_of_Stock_in_QI
                            : 0,

                        CostPerKg = permission.CanViewCost ? s.Price_KG : 0,

                        ExpDate = s.EXP_Date,
                        Unit = sql?.Unit ?? s.Unit,
                        MaterialGroup = materialGroupDict.GetValueOrDefault(s.Material_Code ?? "", ""),
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
            if (permission == null)
                return Unauthorized();

            if (!permission.Page3Access)
                return Forbid();

            var material = request.Material?.Trim();
            var requestedPlant = request.Plant?.Trim();

            if (string.IsNullOrWhiteSpace(material))
                return BadRequest(new { message = "Material is required." });

            var plantsToQuery = GetPlantsForStockMovement(permission, requestedPlant).ToList();

            if (!plantsToQuery.Any())
                return Forbid();

            try
            {
                StockRequirementResponse? mergedSapData = null;

                foreach (var plant in plantsToQuery)
                {
                    var jsonResult = await _sapService.GetStockRequirementAsync(material, plant);

                    var currentSapData = JsonSerializer.Deserialize<StockRequirementResponse>(
                        jsonResult,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (currentSapData == null)
                        continue;

                    if (mergedSapData == null)
                    {
                        mergedSapData = currentSapData;
                    }
                    else if (currentSapData._Detail != null && currentSapData._Detail.Any())
                    {
                        if (mergedSapData._Detail == null)
                            mergedSapData._Detail = currentSapData._Detail;
                        else
                            mergedSapData._Detail.AddRange(currentSapData._Detail);
                    }
                }

                if (mergedSapData == null || mergedSapData._Detail == null || !mergedSapData._Detail.Any())
                {
                    return Ok(new
                    {
                        material_Code = material,
                        plant = requestedPlant ?? string.Join(",", plantsToQuery),
                        _Detail = new List<object>()
                    });
                }

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
                    {
                        materialQuery = materialQuery.Where(x => x.SalesOrganization == salesOrg);
                    }

                    var materialInfo = await materialQuery
                        .Select(x => new
                        {
                            x.Material,
                            x.MaterialGroup1,
                            x.MaterialGroupName,
                            x.SalesGroup
                        })
                        .Distinct()
                        .ToListAsync();

                    var materialDict = materialInfo
                        .GroupBy(x => x.Material)
                        .ToDictionary(g => g.Key, g => g.First());

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

            return Ok(data);
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
            if (keyword.Length < 3) // อย่า query ถ้าสั้นเกินไป
                return Ok(new List<object>());

            var like = $"%{keyword}%";
            _context.Database.SetCommandTimeout(60); //
            var items = await query
                .Where(x =>
                    EF.Functions.Like(x.Material, like) ||
                    EF.Functions.Like(x.MaterialName, like))
                .Select(x => new
                {
                    Code = x.Material,
                    Name = x.MaterialName
                })
                .Distinct()
                .Take(20)
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

            // ── ❌ ลบ GroupJoin ออกทั้งหมด แล้วใช้ 2 query แทน ────────────────────

            // Step 1: count และดึง sales data
            var totalCount = await query.CountAsync();

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

            // Step 2: หา material codes จาก sales ที่ดึงมา
            var materialCodes = salesItems
                .Where(x => x.Material != null)
                .Select(x => x.Material!)
                .Distinct()
                .ToList();

            // Step 3: ดึง NetWeight แยกต่างหาก → ไม่ join ใน DB
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

            // Step 4: map รวมกันใน memory
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

            return Ok(new
            {
                totalCount,
                page = request.Page,
                pageSize = request.PageSize,
                items
            });
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
    }

}
