using Microsoft.AspNetCore.Mvc;

namespace sapaXPrintCloud.Controllers
{
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet("/health")]
        public IActionResult Health()
        {
            return Ok("OK");
        }
    }
}
