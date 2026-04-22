using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Services;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net.ServerSentEvents;
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
    .Select(x => new SalesOrderResponseDto
    {
        BillingDocument = x.BillingDocument,
        BillingDocumentDate = x.BillingDocumentDate,
        DeliveryDate = x.DeliveryDate,         // ← เพิ่ม
        SalesOrderDocument = x.SalesOrderDocument,
        SoldToParty = x.SoldToParty,
        SoldToName = x.SoldToName,
        SoldToMappingAddress = x.SoldToMappingAddress,
        ShiptoCode = x.ShiptoCode,
        ShipToName = x.ShipToName,
        ShipToMappingAddress = x.ShipToMappingAddress,
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

            if (request.DeliveryDateFrom.HasValue)
            {
                var from = request.DeliveryDateFrom.Value.Date;
                query = query.Where(x => x.DeliveryDate >= from);
            }

            if (request.DeliveryDateTo.HasValue)
            {
                var to = request.DeliveryDateTo.Value.Date.AddDays(1);
                query = query.Where(x => x.DeliveryDate < to);
            }

            if (!string.IsNullOrWhiteSpace(request.Document))
            {
                var keyword = $"%{request.Document.Trim()}%";
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
            var scope = ResolveScope(permission);
            var skipMaterialFilter = scope == DataScopes.Company
                                  || scope == DataScopes.CrossCompany
                                  || scope == DataScopes.Division; // ← เพิ่มตรงนี้

            if (!skipMaterialFilter)
            {
                var allowedMaterials = await GetAllowedMaterialsAsync(permission);
                if (allowedMaterials != null &&
                    !string.IsNullOrWhiteSpace(material) &&
                    !allowedMaterials.Contains(material))
                    return Forbid();
            }
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
                    x.ProductGroup,
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
                        MaterialGroupName = g.First().ProductGroup ?? "",
                        SalesGroup = string.Join(", ",
                            g.Where(x => !string.IsNullOrWhiteSpace(x.SalesGroup))
                             .Select(x => x.SalesGroup)
                             .Distinct())
                    });

            var items = sapData
                .Where(s => string.IsNullOrEmpty(request.Plant) ||
                s.Plant == request.Plant ||
                s.Plant == GetCompanySalesOrg(permission.CompanyID))  
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
            StorageLocation = s.Storage_Loc ?? "-",  // ✅ ส่งค่าจริง
            Unrestricted_Stock = s.Unrestricted_Stock,
            QualityInspection = s.Stock_in_QI,
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
    .OrderBy(x => x.BatchNo)
    .ThenBy(x => x.StorageLocation)
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

            var allowedMaterials = await GetAllowedMaterialsForMovementAsync(permission);
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
                if (mergedSapData._Detail != null)
                {
                    mergedSapData._Detail = mergedSapData._Detail
                        .Where(x => !string.Equals(x.Plant, "1900", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // ✅ เช็คอีกครั้งหลัง filter
                if (!mergedSapData._Detail.Any())
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
                        .Select(x => new { x.Material,  x.ProductGroup, x.SalesGroup })
                        .Distinct()
                        .ToListAsync();

                    var materialDict = materialInfo
                        .GroupBy(x => x.Material)
                        .ToDictionary(g => g.Key, g => new
                        {
                            
                            g.First().ProductGroup,
                            SalesGroup = string.Join(", ",
                                g.Where(x => !string.IsNullOrWhiteSpace(x.SalesGroup))
                                 .Select(x => x.SalesGroup).Distinct())
                        });

                    foreach (var item in mergedSapData._Detail)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Material_Code) &&
                            materialDict.TryGetValue(item.Material_Code, out var info))
                        {
                            
                            item.MaterialGroupDescription = info.ProductGroup;
                            item.SalesGroup = info.SalesGroup;
                        }
                    }
                }

                // ── เทียบ RefDoc กับ OpSalesOrder ─────────────────────────────────────
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
    currentScope != DataScopes.Company)
                {
                    // ✅ หา SalesOrder จาก refDocs ผ่าน Op_SalesOrder ก่อน
                    var salesOrdersFromRefDocs = await _context.View_Sales_with_Op_SalesOrder
                        .AsNoTracking()
                        .Where(x => x.PartnerFunction == "Z2" &&
                                    x.Customer != null &&
                                   (EF.Functions.Like(x.Customer, "MGT%") ||
                                    EF.Functions.Like(x.Customer, "GLC%")) &&
                                    x.SalesOrder != null &&
                                    refDocs.Contains(x.SalesOrder))
                        .Select(x => x.SalesOrder!)
                        .Distinct()
                        .ToListAsync();
                    // ก่อน if (salesOrdersFromRefDocs.Any())
                   
                    if (salesOrdersFromRefDocs.Any())
                    {
                        if (currentScope == DataScopes.Division)
                        {
                            // ✅ Division: filter ด้วย allowedUsernames ใน division
                            var allowedUsernames = await GetAllowedUsernamesAsync(permission, currentScope);

                            ownRefDocs = (await _context.View_Sales_with_Op_SalesOrder
                                .AsNoTracking()
                                .Where(x => x.PartnerFunction == "Z2" &&
                                            x.Customer != null &&
                                            allowedUsernames.Contains(x.Customer.Trim().ToUpper()) &&
                                            x.SalesOrder != null &&
                                            salesOrdersFromRefDocs.Contains(x.SalesOrder))
                                .Select(x => x.SalesOrder!)
                                .Distinct()
                                .ToListAsync())
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        }
                        else
                        {
                            // ✅ Own: filter ด้วย View_MGT_GLC_ALL_Sales ตาม scope
                            var ownDocQuery = _context.View_MGT_GLC_ALL_Sales
                                .AsNoTracking()
                                .Where(x => x.SalesDocument != null &&
                                            salesOrdersFromRefDocs.Contains(x.SalesDocument));

                            ownDocQuery = ApplySalesPermission(ownDocQuery, permission);

                            ownRefDocs = (await ownDocQuery
                                .Select(x => x.SalesDocument!)
                                .Distinct()
                                .ToListAsync())
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            // fallback username สำหรับ Own
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

                                foreach (var doc in ownOpDocs)
                                    ownRefDocs.Add(doc);
                            }
                        }
                    }

                    Console.WriteLine($"[DEBUG] ownRefDocs.Count={ownRefDocs.Count}");
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
                        .Select(x => new {
                            x.SalesOrder,
                            x.Customer,
                            x.SoldToParty,
                            x.PurchaseOrderByCustomer
                        })
                        .ToListAsync();

                    var opSalesLookup = opSalesData
                        .GroupBy(x => x.SalesOrder!)
                        .ToDictionary(g => g.Key, g => g.First());

                    // ✅ trimmed → ใช้ใน dict key
                    var soldToParties = opSalesData
                        .Select(x => x.SoldToParty?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct()
                        .ToList();

                    // ✅ raw (ไม่ trim) → ใช้ใน SQL Contains
                    var soldToPartiesRaw = opSalesData
                        .Select(x => x.SoldToParty)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct()
                        .ToList();

                    var customerInfoDict = new Dictionary<string, (string FullName, string IndustryCode, string IndustryName)>
                        (StringComparer.OrdinalIgnoreCase);

                    if (soldToParties.Any())
                    {
                        var customerData = await _context.View_MGT_GLC_ALL_Sales
                            .AsNoTracking()
                            .Where(x => x.SoldToParty != null &&
                                        soldToPartiesRaw.Contains(x.SoldToParty))
                            .Select(x => new
                            {
                                SoldToParty = x.SoldToParty!.Trim(),
                                x.CustomerFullName,
                                x.IndustryCode,
                                x.IndustryName
                            })
                            .Distinct()
                            .ToListAsync();

                        customerInfoDict = customerData
                            .Where(x => !string.IsNullOrWhiteSpace(x.SoldToParty))
                            .GroupBy(x => x.SoldToParty.Trim())
                            .ToDictionary(
                                g => g.Key,
                                g => (
                                    FullName: g.First().CustomerFullName ?? "",
                                    IndustryCode: g.First().IndustryCode ?? "",
                                    IndustryName: g.First().IndustryName ?? ""
                                )
                            );

                        // ✅ fallback จาก Mapping_Soldtos สำหรับ SO ที่ยังไม่ Billing
                        var missingSoldTos = soldToParties
                            .Where(s => !customerInfoDict.ContainsKey(s))
                            .ToList();

                        if (missingSoldTos.Any())
                        {
                            var mappingData = await _context.Mapping_Soldtos
                                .AsNoTracking()
                                .Where(x => x.Customer != null &&
                                            missingSoldTos.Contains(x.Customer.Trim()))
                                .Select(x => new
                                {
                                    Customer = x.Customer!.Trim(),
                                    x.FullName
                                })
                                .Distinct()
                                .ToListAsync();
                            Console.WriteLine($"[DEBUG MAPPING] mappingData.Count={mappingData.Count}");
                            foreach (var m in mappingData.Take(5))
                                Console.WriteLine($"[DEBUG MAPPING] Customer={m.Customer} FullName={m.FullName}");
                            foreach (var m in mappingData)
                            {
                                if (!string.IsNullOrWhiteSpace(m.Customer) &&
                                    !customerInfoDict.ContainsKey(m.Customer))
                                {
                                    customerInfoDict[m.Customer] = (
                                        FullName: m.FullName ?? "",
                                        IndustryCode: "",
                                        IndustryName: ""
                                    );
                                }
                            }
                        }
                    }

                    var dept = permission.Department?.ToLower() ?? "";
                    var isPurchase = dept == "purchase";

                    foreach (var item in mergedSapData._Detail)
                    {
                        var refDoc = item.Ref_Doc?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(refDoc)) continue;

                        bool canViewCustomer = currentScope == DataScopes.CrossCompany ||
                        currentScope == DataScopes.Company ||
                        (permission.CanViewCustomer && ownRefDocs.Contains(refDoc));
                        if (opSalesLookup.TryGetValue(refDoc, out var opSales))
                        {
                            var soldTo = opSales.SoldToParty?.Trim() ?? "";

                            if (canViewCustomer)
                            {
                                if (customerInfoDict.TryGetValue(soldTo, out var info))
                                {
                                    item.Customer_Code = soldTo;
                                    item.Customer_Name = info.FullName;
                                    item.IndustryCode = info.IndustryCode;
                                    item.IndustryName = info.IndustryName;
                                }
                                else
                                {
                                    item.Customer_Code = soldTo;
                                    item.Customer_Name = soldTo;
                                }
                            }
                            else
                            {
                                // ✅ Purchase เห็น Code, Sales ซ่อนทั้งหมด
                                item.Customer_Code = isPurchase ? soldTo : null;
                                item.Customer_Name = null;
                                item.IndustryCode = null;
                                item.IndustryName = null;
                            }
                        }
                        else
                        {
                            if (!canViewCustomer)
                            {
                                // ✅ Purchase คง SAP Code ไว้, Sales ซ่อนหมด
                                if (!isPurchase)
                                    item.Customer_Code = null;
                                item.Customer_Name = null;
                                item.IndustryCode = null;
                                item.IndustryName = null;
                            }
                        }
                    }
                }

                // ── CanViewVendor ──────────────────────────────────────────────────────
                if (!permission.CanViewVendor)
                    foreach (var item in mergedSapData._Detail)
                    {
                        item.Vendor_Code = null;
                        item.Vendor_Name = null;
                    }

                // ── CanViewCustomer ────────────────────────────────────────────────────
                if (!permission.CanViewCustomer)
                {
                    var dept = permission.Department?.ToLower() ?? "";
                    foreach (var item in mergedSapData._Detail)
                    {
                        item.Customer_Name = null;
                        item.IndustryCode = null;
                        item.IndustryName = null;

                        // ✅ Purchase เห็น Code, Sales ไม่เห็น Code
                        if (dept != "purchase")
                            item.Customer_Code = null;
                    }
                }

                mergedSapData.Plant = requestedPlant ?? string.Join(",", plantsToQuery);
                // ก่อน return Ok(mergedSapData)
                /*foreach (var d in mergedSapData._Detail)
                {
                    Console.WriteLine($"[STOCK] Ref={d.Ref_Doc} Industry={d.IndustryCode}/{d.IndustryName}");
                }*/
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
        [HttpGet("MaterialLookup")]
        public async Task<IActionResult> GetMaterialLookup([FromQuery] string keyword)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null) return Unauthorized();

            if (!permission.Page3Access && !permission.Page2Access)
                return Forbid();

            keyword = keyword?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
                return Ok(new List<object>());

            // ✅ ทุก scope ค้นหาได้ทั้งหมดใน SalesOrg ของตัวเอง
            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.Material != null);

            var scope = ResolveScope(permission);
            if (scope == DataScopes.CrossCompany)
            {
                // CrossCompany ไม่ filter SalesOrg
            }
            else
            {
                // ทุกคนเห็น material ใน SalesOrg ของตัวเอง
                query = query.Where(x => x.SalesOrganization ==
                          NormalizeKey(permission.SalesOrganizationCode));
            }

            var likeStart = $"{keyword}%";
            var likeAny = $"%{keyword}%";
            _context.Database.SetCommandTimeout(60);

            var items = await query
                .Where(x =>
                    EF.Functions.Like(x.Material, likeAny) ||
                    EF.Functions.Like(x.MaterialName, likeAny))
                .Select(x => new
                {
                    Code = x.Material,
                    Name = x.MaterialName,
                    Priority = EF.Functions.Like(x.Material, likeStart) ? 0
                             : EF.Functions.Like(x.MaterialName, likeStart) ? 1
                             : 2
                })
                .Distinct()
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Take(20)
                .Select(x => new { x.Code, x.Name })
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
                .Where(x => x.ProductGroup != null && x.ProductGroup != "");

            if (!string.IsNullOrWhiteSpace(request.ProductGroup))
            {
                query = query.Where(x => x.ProductGroup == request.ProductGroup.Trim());
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
        [HttpPost("SalesOrderDetail")]
        public async Task<IActionResult> GetSalesOrderDetail([FromBody] SalesOrderDetailRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            try
            {
                var result = await _sapService.GetSalesOrderByIdAsync(request.SalesOrder);
                return Content(result, "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [Authorize]
        [HttpPost("SalesOrderItem")]
        public async Task<IActionResult> GetSalesOrderItem([FromBody] SalesOrderDetailRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            try
            {
                var result = await _sapService.GetSalesOrderItemAsync(request.SalesOrder);
                return Content(result, "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [Authorize]
        [HttpGet("ProductGroupLookup")]
        public async Task<IActionResult> GetProductGroupLookup([FromQuery] string keyword)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null) return Unauthorized();

            if (!permission.Page1Access && !permission.Page2Access) return Forbid();

            keyword = keyword?.Trim() ?? "";

            if (keyword.Length < 2)
                return Ok(new List<object>());

            var likeAny = $"%{keyword}%";
            var likeStart = $"{keyword}%";

            _context.Database.SetCommandTimeout(60);

            var query = _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.ProductGroup != null && x.ProductGroup != "");

            query = ApplySalesPermission(query, permission);

            var items = await query
                .Where(x => EF.Functions.Like(x.ProductGroup, likeAny))
                .Select(x => new
                {
                    Code = x.ProductGroup!,
                    Priority = EF.Functions.Like(x.ProductGroup, likeStart) ? 0 : 1
                })
                .Distinct()
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Take(20)
                .Select(x => new { x.Code, Name = x.Code }) // ✅ ถ้าไม่มี ProductGroupName ใช้ Code แทน
                .ToListAsync();

            return Ok(items);
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
        [Authorize]
        [HttpPost("SalesOrderFull")]
        public async Task<IActionResult> GetSalesOrderFull([FromBody] SalesOrderRequestDto request)
        {
            var permission = await GetCurrentPermissionAsync();
            if (permission == null)
                return Unauthorized();

            if (!permission.Page1Access)
                return Forbid();

            bool hasDocument = !string.IsNullOrWhiteSpace(request.Document);
            bool hasDeliveryDate = request.DeliveryDateFrom.HasValue && request.DeliveryDateTo.HasValue;

            if (!hasDocument && !hasDeliveryDate)
                return BadRequest(new { message = "Please provide Sales Order number or Delivery Date range" });

            try
            {
                var scope = ResolveScope(permission);

                // Step 1: ดึงจาก Op_SalesOrder
                var soQuery = _context.Op_SalesOrders.AsNoTracking();

                if (!string.IsNullOrEmpty(request.SalesOrganization))
                    soQuery = soQuery.Where(x => x.SalesOrganization == request.SalesOrganization);

                if (hasDocument)
                    soQuery = soQuery.Where(x => x.SalesOrder.Contains(request.Document!.Trim()));

                if (hasDeliveryDate)
                {
                    soQuery = soQuery.Where(x => x.RequestedDeliveryDate >= request.DeliveryDateFrom!.Value.Date);
                    soQuery = soQuery.Where(x => x.RequestedDeliveryDate < request.DeliveryDateTo!.Value.Date.AddDays(1));
                }

                if (!string.IsNullOrWhiteSpace(request.SoldToParty))
                    soQuery = soQuery.Where(x => x.SoldToParty == request.SoldToParty.Trim());

                // ✅ Filter SalesOrganization ตาม Company (ยกเว้น CrossCompany)
                if (scope != DataScopes.CrossCompany)
                {
                    var salesOrg = NormalizeKey(permission.SalesOrganizationCode);
                    if (string.IsNullOrWhiteSpace(salesOrg))
                        return Ok(new { totalCount = 0, page = request.Page, pageSize = request.PageSize, items = new List<object>() });

                    soQuery = soQuery.Where(x => x.SalesOrganization == salesOrg);
                }

                // ✅ Filter ตาม Division/Own scope
                if (scope == DataScopes.Own || scope == DataScopes.Division)
                {
                    var allowedUsernames = await GetAllowedUsernamesAsync(permission, scope);

                    var allowedSalesOrders = await _context.View_Sales_with_Op_SalesOrder
                        .AsNoTracking()
                        .Where(x => x.PartnerFunction == "Z2" &&
                                    x.Customer != null &&
                                    allowedUsernames.Contains(x.Customer.Trim().ToUpper()) &&
                                    x.SalesOrder != null)
                        .Select(x => x.SalesOrder!)
                        .Distinct()
                        .ToListAsync();

                    soQuery = soQuery.Where(x => allowedSalesOrders.Contains(x.SalesOrder));
                }

                var soList = await soQuery
                    .OrderByDescending(x => x.RequestedDeliveryDate)
                    .Select(x => new
                    {
                        x.SalesOrder,
                        x.RequestedDeliveryDate,
                        x.SoldToParty,
                        x.SalesOrderDate,
                        x.SalesOrganization
                    })
                    .ToListAsync();

                // ✅ ถ้า Document ไม่มีใน DB → ดึงจาก SAP โดยตรง (เฉพาะ scope ที่เหมาะสม)
                if (hasDocument && !soList.Any())
                {
                    // ✅ SAP fallback — ตรวจสอบว่า sales employee ใน SAP อยู่ใน scope ไหม
                    try
                    {
                        var sapJson = await _sapService.GetSalesOrderFullAsync(request.Document!.Trim());
                        var sapDoc = JsonDocument.Parse(sapJson);
                        var d = sapDoc.RootElement.GetProperty("d");

                        Console.WriteLine($"[DEBUG SAP] {sapJson}");

                        var soNumber = d.GetProperty("SalesOrder").GetString() ?? "";
                        var soldToParty = d.TryGetProperty("SoldToParty", out var sp) ? sp.GetString() ?? "" : "";
                        var salesOrg = d.TryGetProperty("SalesOrganization", out var so) ? so.GetString() ?? "" : "";

                        DateTime? parsedDelivery = null;
                        DateTime? parsedSoDate = null;

                        if (d.TryGetProperty("RequestedDeliveryDate", out var dd))
                        {
                            var ms = System.Text.RegularExpressions.Regex.Match(dd.GetString() ?? "", @"\d+");
                            if (ms.Success && long.TryParse(ms.Value, out var ticks))
                                parsedDelivery = DateTimeOffset.FromUnixTimeMilliseconds(ticks).UtcDateTime;
                        }

                        if (d.TryGetProperty("SalesOrderDate", out var sd))
                        {
                            var ms = System.Text.RegularExpressions.Regex.Match(sd.GetString() ?? "", @"\d+");
                            if (ms.Success && long.TryParse(ms.Value, out var ticks))
                                parsedSoDate = DateTimeOffset.FromUnixTimeMilliseconds(ticks).UtcDateTime;
                        }

                        string shipToCode = "";
                        string salesEmployeeCode = "";

                        if (d.TryGetProperty("to_Partner", out var toPartner) &&
                            toPartner.TryGetProperty("results", out var partners))
                        {
                            foreach (var p in partners.EnumerateArray())
                            {
                                var pfc = p.TryGetProperty("PartnerFunctionInternalCode", out var pfcVal)
                                               ? pfcVal.GetString() : "";
                                var customer = p.TryGetProperty("Customer", out var cVal)
                                               ? cVal.GetString() ?? "" : "";

                                if (pfc == "WE") shipToCode = customer;
                                if (pfc == "Z2") salesEmployeeCode = customer;
                            }
                        }

                        // ✅ ตรวจสอบ scope สำหรับ SAP fallback
                        if (scope == DataScopes.Own || scope == DataScopes.Division)
                        {
                            var allowedUsernames = await GetAllowedUsernamesAsync(permission, scope);
                            if (!string.IsNullOrWhiteSpace(salesEmployeeCode) &&
                                !allowedUsernames.Contains(salesEmployeeCode.Trim().ToUpper()))
                            {
                                return Ok(new { totalCount = 0, page = request.Page, pageSize = request.PageSize, items = new List<object>() });
                            }
                        }

                        string soldToName = "", soldToAddress = "";
                        if (!string.IsNullOrEmpty(soldToParty))
                        {
                            var dbSoldTo = await _context.View_MGT_GLC_ALL_Sales
                                .AsNoTracking()
                                .Where(x => x.SoldToParty == soldToParty)
                                .Select(x => new { x.SoldToName, SoldToAddress = x.SoldtoMappingAddress })
                                .FirstOrDefaultAsync();
                            soldToName = dbSoldTo?.SoldToName ?? "";
                            soldToAddress = dbSoldTo?.SoldToAddress ?? "";
                        }

                        string shipToName = "", shipToAddress = "";
                        if (!string.IsNullOrEmpty(shipToCode))
                        {
                            var dbShipTo = await _context.View_MGT_GLC_ALL_Sales
                                .AsNoTracking()
                                .Where(x => x.ShiptoCode == shipToCode)
                                .Select(x => new { x.ShipToName, ShipToAddress = x.ShiptoMappingAddress })
                                .FirstOrDefaultAsync();
                            shipToName = dbShipTo?.ShipToName ?? "";
                            shipToAddress = dbShipTo?.ShipToAddress ?? "";
                        }

                        string salesEmployeeName = "";
                        if (!string.IsNullOrEmpty(salesEmployeeCode))
                        {
                            var dbUser = await _context.MsUsers
                                .AsNoTracking()
                                .Where(x => x.Username == salesEmployeeCode)
                                .Select(x => x.FullName)
                                .FirstOrDefaultAsync();
                            salesEmployeeName = dbUser ?? salesEmployeeCode;
                        }

                        string sapMaterial = "", sapMaterialName = "";
                        string sapPurchaseOrderByCustomer = "", sapRequestedQuantityUnit = "";
                        decimal? sapOrderQuantity = null;

                        if (d.TryGetProperty("to_Item", out var toItem) &&
                            toItem.TryGetProperty("results", out var sapItems2) &&
                            sapItems2.GetArrayLength() > 0)
                        {
                            var firstItem = sapItems2[0];
                            sapMaterial = firstItem.TryGetProperty("Material", out var matProp) ? matProp.GetString() ?? "" : "";
                            sapPurchaseOrderByCustomer = firstItem.TryGetProperty("PurchaseOrderByCustomer", out var pocProp) ? pocProp.GetString() ?? "" : "";
                            sapRequestedQuantityUnit = firstItem.TryGetProperty("RequestedQuantityUnit", out var rquProp) ? rquProp.GetString() ?? "" : "";

                            if (firstItem.TryGetProperty("RequestedQuantity", out var rqProp) &&
                                decimal.TryParse(rqProp.GetString(), System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var rqVal))
                                sapOrderQuantity = rqVal;

                            if (!string.IsNullOrEmpty(sapMaterial))
                            {
                                var matDesc = await _context.View_MaterialStock_WeightKG
                                    .AsNoTracking()
                                    .Where(x => x.Material == sapMaterial)
                                    .Select(x => x.MaterialDescription)
                                    .FirstOrDefaultAsync();
                                sapMaterialName = matDesc ?? "";
                            }
                        }

                        if (!string.IsNullOrEmpty(soNumber))
                        {
                            return Ok(new
                            {
                                totalCount = 1,
                                page = request.Page,
                                pageSize = request.PageSize,
                                items = new[]
                                {
                            new
                            {
                                SalesOrderDocument       = soNumber,
                                DocDate                  = parsedSoDate,
                                DeliveryDate             = parsedDelivery,
                                BillingDocument          = "",
                                BillingDocumentDate      = (DateTime?)null,
                                SoldToParty              = soldToParty,
                                SoldToName               = soldToName,
                                SoldToAddress            = soldToAddress,
                                ShiptoCode               = shipToCode,
                                ShipToName               = shipToName,
                                ShipToAddress            = shipToAddress,
                                Material                 = sapMaterial,
                                MaterialName             = sapMaterialName,
                                SalesEmployee            = salesEmployeeName,
                                PurchaseOrderByCustomer  = sapPurchaseOrderByCustomer,
                                OrderQuantity            = sapOrderQuantity,
                                RequestedQuantityUnit    = sapRequestedQuantityUnit
                            }
                        }
                            });
                        }
                    }
                    catch { }
                }

                if (!soList.Any())
                    return Ok(new { totalCount = 0, page = request.Page, pageSize = request.PageSize, items = new List<object>() });

                var soNumbers = soList.Select(x => x.SalesOrder).ToList();

                // Step 2: ดึงจาก View_MGT_GLC_ALL_Sales
                var viewData = await _context.View_MGT_GLC_ALL_Sales
                    .AsNoTracking()
                    .Where(x => x.SalesDocument != null && soNumbers.Contains(x.SalesDocument))
                    .Select(x => new
                    {
                        x.SalesDocument,
                        x.BillingDocument,
                        x.BillingDocumentDate,
                        x.SoldToParty,
                        x.SoldToName,
                        SoldToAddress = x.SoldtoMappingAddress,
                        x.ShiptoCode,
                        x.ShipToName,
                        ShipToAddress = x.ShiptoMappingAddress,
                        x.Material,
                        x.MaterialName,
                        x.SalesEmployeeID
                    })
                    .ToListAsync();

                // Step 3: ดึงชื่อ/ที่อยู่จาก Mapping
                var mappingSoldTo = await _context.Mapping_Soldtos
                    .AsNoTracking()
                    .Where(x => soNumbers.Contains(x.SalesOrder!))
                    .Select(x => new { x.SalesOrder, x.Customer, x.FullName, x.Address })
                    .ToListAsync();

                var mappingShipTo = await _context.Mapping_Shiptos
                    .AsNoTracking()
                    .Where(x => soNumbers.Contains(x.SalesOrder!) && x.PartnerFunctionInternalCode == "WE")
                    .Select(x => new { x.SalesOrder, x.Customer, x.FullName, x.Address })
                    .ToListAsync();

                // Step 4: ดึง SalesEmployee + PurchaseOrderByCustomer
                var z2Partners = await _context.View_Sales_with_Op_SalesOrder
                    .AsNoTracking()
                    .Where(x => x.PartnerFunction == "Z2" && soNumbers.Contains(x.SalesOrder!))
                    .Select(x => new { x.SalesOrder, x.Customer, x.PurchaseOrderByCustomer })
                    .ToListAsync();

                // ✅ filter z2Partners ตาม scope
                if (scope == DataScopes.Own || scope == DataScopes.Division)
                {
                    var allowedUsernames = await GetAllowedUsernamesAsync(permission, scope);
                    z2Partners = z2Partners
                        .Where(x => allowedUsernames.Contains(x.Customer?.Trim().ToUpper() ?? ""))
                        .ToList();
                }

                var z2Usernames = z2Partners.Select(x => x.Customer).Distinct().ToList();
                var users = await _context.MsUsers
                    .AsNoTracking()
                    .Where(x => z2Usernames.Contains(x.Username))
                    .Select(x => new { x.Username, x.FullName })
                    .ToListAsync();

                // Step 5: ดึง MaterialName
                // Step 5b: ย้ายขึ้นมาก่อน เพื่อให้ได้ sapItemList
                var sapItemList = await _sapService.GetSalesOrderItemsAsync(soNumbers);

                // Step 5: รวม material codes จากทั้ง 2 แหล่ง
                var materialCodes = viewData.Select(x => x.Material)
                    .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

                var sapMaterialCodesForDesc = sapItemList
                    .Where(x => !string.IsNullOrWhiteSpace(x.Material))
                    .Select(x => x.Material!).Distinct().ToList();

                // ✅ ใช้ allMaterialCodes query แทน materialCodes
                var allMaterialCodes = materialCodes.Union(sapMaterialCodesForDesc).Distinct().ToList();

                var materialDict = new Dictionary<string, string>();
                if (allMaterialCodes.Any())  // ✅ เปลี่ยนจาก materialCodes.Any()
                {
                    var materialInfo = await _context.View_MaterialStock_WeightKG
                        .AsNoTracking()
                        .Where(x => x.Material != null && allMaterialCodes.Contains(x.Material)) // ✅ ใช้ allMaterialCodes
                        .GroupBy(x => x.Material)
                        .Select(g => new { Material = g.Key, MaterialDescription = g.First().MaterialDescription })
                        .ToListAsync();

                    materialDict = materialInfo
                        .Where(x => x.Material != null)
                        .ToDictionary(x => x.Material!, x => x.MaterialDescription ?? "");

                    // ✅ Fallback: View_MGT_GLC_ALL_Sales สำหรับที่ยังหาไม่เจอ
                    var missing = allMaterialCodes
                        .Where(m => !string.IsNullOrEmpty(m) && !materialDict.ContainsKey(m!))
                        .ToList();

                    if (missing.Any())
                    {
                        var fallback = await _context.View_MGT_GLC_ALL_Sales
                            .AsNoTracking()
                            .Where(x => x.Material != null && missing.Contains(x.Material))
                            .Select(x => new { x.Material, x.MaterialName })
                            .Distinct()
                            .ToListAsync();

                        foreach (var f in fallback)
                            if (!string.IsNullOrEmpty(f.Material) && !materialDict.ContainsKey(f.Material))
                                materialDict[f.Material] = f.MaterialName ?? "";
                    }
                }

                // sapMaterialDict ไม่จำเป็นต้องแยกอีกต่อไป เพราะรวมใน materialDict แล้ว
                var sapMaterialDict = materialDict; // ✅ ใช้ dict เดียวกัน
                var sapMaterialCodes = sapMaterialCodesForDesc;

                // Step 6: รวมข้อมูล — ✅ filter เฉพาะ SO ที่มี z2 ที่ allowed
                var allowedSalesOrderNumbers = scope == DataScopes.Own || scope == DataScopes.Division
                    ? z2Partners.Select(x => x.SalesOrder).ToHashSet()
                    : null;

                // Step 6: รวมข้อมูล
                var items = soList
                    .Where(so => allowedSalesOrderNumbers == null ||
                                 allowedSalesOrderNumbers.Contains(so.SalesOrder))
                    .Select(so =>
                    {
                        var view = viewData.FirstOrDefault(v => v.SalesDocument == so.SalesOrder);
                        var soldTo = mappingSoldTo.FirstOrDefault(m => m.SalesOrder == so.SalesOrder);
                        var shipTo = mappingShipTo.FirstOrDefault(m => m.SalesOrder == so.SalesOrder);
                        var z2 = z2Partners.FirstOrDefault(p => p.SalesOrder == so.SalesOrder);
                        var user = users.FirstOrDefault(u => u.Username == z2?.Customer);
                        var sapItem = sapItemList.FirstOrDefault(s => s.SalesOrder == so.SalesOrder);

                        var material = view?.Material ?? sapItem.Material ?? "";
                        var materialName = "";
                        decimal? orderQuantity = sapItem.OrderQuantity;
                        string requestedQuantityUnit = sapItem.RequestedQuantityUnit ?? "";

                        if (!string.IsNullOrEmpty(material))
                        {
                            // ✅ ลอง materialDict (View_MaterialStock_WeightKG) ก่อน
                            if (!materialDict.TryGetValue(material, out materialName)
                                || string.IsNullOrEmpty(materialName))
                            {
                                // ✅ fallback → sapMaterialDict
                                if (!sapMaterialDict.TryGetValue(material, out materialName)
                                    || string.IsNullOrEmpty(materialName))
                                {
                                    // ✅ fallback สุดท้าย → MaterialName จาก View_MGT_GLC_ALL_Sales
                                    materialName = view?.MaterialName ?? "";
                                }
                            }
                        }
                        else
                        {
                            // ✅ ถ้า view ไม่มี Material → ลองจาก SAP
                            material = sapItem.Material ?? "";
                            if (!string.IsNullOrEmpty(material))
                            {
                                if (!sapMaterialDict.TryGetValue(material, out materialName)
                                    || string.IsNullOrEmpty(materialName))
                                    materialName = "";
                            }
                        }
                        

                        return new
                        {
                            SalesOrderDocument = so.SalesOrder,
                            DocDate = so.SalesOrderDate,
                            DeliveryDate = so.RequestedDeliveryDate,
                            BillingDocument = view?.BillingDocument ?? "",
                            BillingDocumentDate = view?.BillingDocumentDate,
                            SoldToParty = view?.SoldToParty ?? so.SoldToParty ?? "",
                            SoldToName = view?.SoldToName ?? soldTo?.FullName ?? "",
                            SoldToAddress = view?.SoldToAddress ?? soldTo?.Address ?? "",
                            ShiptoCode = view?.ShiptoCode ?? shipTo?.Customer ?? "",
                            ShipToName = view?.ShipToName ?? shipTo?.FullName ?? "",
                            ShipToAddress = view?.ShipToAddress ?? shipTo?.Address ?? "",
                            Material = material,
                            MaterialName = materialName ?? "",
                            SalesEmployee = user?.FullName ?? z2?.Customer ?? "",
                            PurchaseOrderByCustomer = z2?.PurchaseOrderByCustomer ?? "",
                            OrderQuantity = orderQuantity,
                            RequestedQuantityUnit = requestedQuantityUnit
                        };
                    }).ToList();

                // ✅ filter ออกถ้าไม่มี Material (SAP ไม่มีข้อมูล)
                items = items
                    .Where(x => !string.IsNullOrWhiteSpace(x.Material))
                    .ToList();

                // ✅ เพิ่ม — filter ออกถ้า SAP ไม่มี item เลย (results: [])
                var soWithSapItems = sapItemList
                    .Where(x => !string.IsNullOrWhiteSpace(x.Material))
                    .Select(x => x.SalesOrder)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // ✅ เก็บเฉพาะ SO ที่มีข้อมูลใน View (Billing) หรือมี SAP item
                items = items
                    .Where(x =>
                        viewData.Any(v => v.SalesDocument == x.SalesOrderDocument &&
                                          !string.IsNullOrWhiteSpace(v.Material)) ||
                        soWithSapItems.Contains(x.SalesOrderDocument))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(request.Material))
                {
                    var matKeyword = request.Material.Trim().ToUpper();
                    items = items
                        .Where(x =>
                            (x.Material ?? "").ToUpper().Contains(matKeyword) ||
                            (x.MaterialName ?? "").ToUpper().Contains(matKeyword))
                        .ToList();
                }
               
                var filteredTotal = items.Count;
                var pagedItems = items
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                return Ok(new
                {
                    totalCount = filteredTotal,
                    page = request.Page,
                    pageSize = request.PageSize,
                    items = pagedItems
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ✅ Helper Method
        private async Task<HashSet<string>> GetAllowedUsernamesAsync(
            View_UserPermission permission, string scope)
        {
            if (scope == DataScopes.Own)
            {
                return new HashSet<string>(
                    new[] { permission.Username?.Trim().ToUpper() ?? "" },
                    StringComparer.OrdinalIgnoreCase);
            }

            if (scope == DataScopes.Division)
            {
                var division = permission.Division?.Trim().ToUpper() ?? "";
                var salesOrg = permission.SalesOrganizationCode?.Trim().ToUpper() ?? "";

                var usernames = await _context.View_UserPermissions
                    .AsNoTracking()
                    .Where(x =>
                        x.CompanyID == permission.CompanyID &&
                        x.Division != null &&
                        x.Division.Trim().ToUpper() == division &&
                        x.SalesOrganizationCode != null &&
                        x.SalesOrganizationCode.Trim().ToUpper() == salesOrg)
                    .Select(x => x.Username!.Trim().ToUpper())
                    .Distinct()
                    .ToListAsync();

                return new HashSet<string>(usernames, StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                // ✅ ถ้าไม่ระบุ plant → ดึงทุก plant ที่มีสิทธิ์
                if (string.IsNullOrWhiteSpace(requestedPlant))
                    return allPlants;

                return allPlants.Contains(requestedPlant)
                    ? new[] { requestedPlant }
                    : Enumerable.Empty<string>();
            }

            if (!permission.CompanyID.HasValue)
                return Enumerable.Empty<string>();

            if (!CompanyPlants.TryGetValue(permission.CompanyID.Value, out var companyPlants))
                return Enumerable.Empty<string>();

            // ✅ ถ้าไม่ระบุ plant → ดึงทุก plant ของ company นั้น
            if (string.IsNullOrWhiteSpace(requestedPlant))
                return companyPlants;

            return companyPlants.Contains(requestedPlant)
                ? new[] { requestedPlant }
                : Enumerable.Empty<string>();
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
                    DeliveryDate = g.Max(x => x.DeliveryDate),
                    // ไม่เอา SalesEmployee เป็น key
                    // ถ้าซ้ำกันจริง ๆ ส่วนมากค่านี้จะเหมือนกันอยู่แล้ว
                    SalesEmployee = g.Max(x => x.SalesEmployeeID) ?? "-"
                });
        }
        // ✅ สำหรับ StockofMaterial — Division ดูได้หมด
        private async Task<HashSet<string>?> GetAllowedMaterialsAsync(View_UserPermission permission)
        {
            var scope = ResolveScope(permission);

            if (scope == DataScopes.Company
             || scope == DataScopes.CrossCompany
             || scope == DataScopes.Division) // ← StockofMaterial ใช้ skipMaterialFilter แทน
                return null;

            var query = _context.View_MGT_GLC_ALL_Sales.AsNoTracking();
            query = ApplySalesPermission(query, permission);

            var materials = await query
                .Where(x => x.Material != null)
                .Select(x => x.Material!)
                .Distinct()
                .ToListAsync();

            return new HashSet<string>(materials, StringComparer.OrdinalIgnoreCase);
        }

        
        // ✅ สำหรับ StockMovement — Division ดูได้หมดเหมือน StockofMaterial
        private async Task<HashSet<string>?> GetAllowedMaterialsForMovementAsync(View_UserPermission permission)
        {
            // ทุก scope ดู material ได้หมด ไม่จำกัด
            return null;
        }
        [Authorize]
        [HttpPost("debug-stockmovement")]
        public async Task<IActionResult> DebugStockMovement([FromBody] List<string> refDocs)
        {
            // Step 1: ดู opSalesData match ไหม
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

            // Step 2: ดู SoldToParty → Industry
            var soldToParties = opSalesData
                .Select(x => x.SoldToParty?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct()
                .ToList();

            var industryData = await _context.View_MGT_GLC_ALL_Sales
                .AsNoTracking()
                .Where(x => x.SoldToParty != null && soldToParties.Contains(x.SoldToParty))
                .Select(x => new { x.SoldToParty, x.CustomerFullName, x.IndustryCode, x.IndustryName })
                .Distinct()
                .ToListAsync();

            return Ok(new
            {
                refDocs_input = refDocs,
                opSalesData = opSalesData,
                soldToParties = soldToParties,
                industryData = industryData
            });
        }

    }


}
