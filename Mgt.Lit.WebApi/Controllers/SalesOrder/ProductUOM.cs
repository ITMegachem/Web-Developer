using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Helpers;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Mgt.Lit.WebApi.Controllers.ProductUOM
{
    [ServiceFilter(typeof(ActivityLogFilter))]
    [ApiController]
    [Route("api/ProductUOM")]
    public class ProductUnitOfMeasureController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public ProductUnitOfMeasureController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ── POST api/ProductUOM/token ─────────────────────────────────────────────
        [HttpPost("token")]
        public IActionResult GetToken([FromBody] LoginRequestDto dto)
        {
            var validUser = _config["ProductUOM:ApiUser"];
            var validKey = _config["ProductUOM:ApiKey"];

            if (dto.Username != validUser || dto.Password != validKey)
                return Unauthorized(new { message = "Invalid credentials" });

            var token = JwtHelper.GenerateToken(
                new MsUser
                {
                    Username = dto.Username,
                    FullName = "Zoho API",
                    UserRole = "api",
                    TokenVersion = "zoho-static-token-v1"  // ✅ เพิ่ม static version
                },
                _config,
                primaryCompanyId: null,
                primaryCompanyCode: null,
                allCompanies: new List<UserCompanyDto>(),
                permission: new UserPermissionDto
                {
                    Page2Access = true
                });

            return Ok(new { token });
        }
        // ── GET api/ProductUOM ────────────────────────────────────────────────────
        // ── GET api/ProductUOM ────────────────────────────────────────────────────
        [Authorize]
        [RequirePageAccess("Page2Access")]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var items = await (
                    from uom in _context.Ms_ProductUnitsOfMeasure
                    join desc in _context.Ms_ProductDescription
                        on new { uom.Product, Language = "EN" }
                        equals new { desc.Product, desc.Language }
                        into descGroup
                    from d in descGroup.DefaultIfEmpty()
                    select new
                    {
                        uom.Id,
                        uom.Product,
                        ProductDescription = d.ProductDescription,
                        uom.AlternativeUnit,
                        uom.QuantityDenominator,
                        uom.QuantityNumerator,
                        uom.BaseUnit,
                        uom.GrossWeight,
                        uom.WeightUnit,
                        uom.UpdateDate
                    }
                )
                .AsNoTracking()
                .Take(1000)
                .ToListAsync();

                return Ok(new { totalCount = items.Count, items });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ── GET api/ProductUOM/Product('EMCO01-CN-DR-01') ────────────────────────
        [Authorize]
        [RequirePageAccess("Page2Access")]
        [HttpGet("Product('{productCode}')")]
        public async Task<IActionResult> GetByProduct(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return BadRequest(new { message = "ProductCode is required." });

            try
            {
                var items = await (
                    from uom in _context.Ms_ProductUnitsOfMeasure
                    where uom.Product == productCode.Trim()
                    join desc in _context.Ms_ProductDescription
                        on new { uom.Product, Language = "EN" }
                        equals new { desc.Product, desc.Language }
                        into descGroup
                    from d in descGroup.DefaultIfEmpty()
                    select new
                    {
                        uom.Id,
                        uom.Product,
                        ProductDescription = d.ProductDescription,
                        UsageUnit = uom.AlternativeUnit,
                        uom.QuantityDenominator,
                        SKUUnit = uom.QuantityNumerator, 
                        RelateUnit = uom.BaseUnit,
                        uom.GrossWeight,
                        uom.WeightUnit,
                        uom.UpdateDate
                    }
                )
                .AsNoTracking()
                .ToListAsync();

                if (!items.Any())
                    return NotFound(new { message = $"ProductCode '{productCode}' not found" });

                return Ok(new { totalCount = items.Count, items });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ── GET api/ProductUOM/{id} ───────────────────────────────────────────────
        [Authorize]
        [RequirePageAccess("Page2Access")]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            if (id <= 0)
                return BadRequest(new { message = "Id must be greater than 0." });

            try
            {
                var item = await (
                    from uom in _context.Ms_ProductUnitsOfMeasure
                    where uom.Id == id
                    join desc in _context.Ms_ProductDescription
                        on new { uom.Product, Language = "EN" }
                        equals new { desc.Product, desc.Language }
                        into descGroup
                    from d in descGroup.DefaultIfEmpty()
                    select new
                    {
                        uom.Id,
                        uom.Product,
                        ProductDescription = d.ProductDescription,
                        UsageUnit = uom.AlternativeUnit,
                        uom.QuantityDenominator,
                        SKUUnit = uom.QuantityNumerator, // ✅ 25
                        RelateUnit = uom.BaseUnit,
                        uom.GrossWeight,
                        uom.WeightUnit,
                        uom.UpdateDate
                    }
                )
                .AsNoTracking()
                .FirstOrDefaultAsync();

                if (item is null)
                    return NotFound(new { message = $"Id '{id}' not found" });

                return Ok(item);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}