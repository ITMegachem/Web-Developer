using Mgt.Lit.Core.Data;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Mgt.Lit.WebApi.Controllers.Member
{
    [ServiceFilter(typeof(ActivityLogFilter))]
    [ApiController]
    [Route("api/users")]

    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(_context.MsUsers.ToList());
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                Username = User.Identity?.Name,
                Role = User.FindFirst(ClaimTypes.Role)?.Value,
                Division = User.FindFirst("Division")?.Value
            });
        }
    }

}
