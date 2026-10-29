using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mgt.Lit.WebApi.Controllers
{
    // Heartbeat for the WebFront (every ~30 s per open tab): 200 while this token is still the user's
    // current session, 401 once TokenVersion has moved on (signed in on another device) — the JWT
    // OnTokenValidated check in Program.cs does the work. Deliberately NOT under ActivityLogFilter so
    // the heartbeat doesn't flood the activity log.
    [ApiController]
    [Authorize]
    [Route("api/member")]
    public class SessionCheckController : ControllerBase
    {
        [HttpGet("session-check")]
        public IActionResult Check() => Ok(new { ok = true });
    }
}
