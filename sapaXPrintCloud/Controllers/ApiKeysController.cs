using Microsoft.AspNetCore.Mvc;
using sapaXPrintCloud.Services;

namespace sapaXPrintCloud.Controllers
{
    [ApiController]
    [Route("api/keys")]
    public class ApiKeysController : ControllerBase
    {
        [HttpPost("powerapps")]
        public IActionResult CreatePowerAppsKey()
        {
            var key = ApiKeyService.Generate("pa_live");

            Console.WriteLine($"POWERAPPS API KEY GENERATED: {key}");

            return Ok(new
            {
                apiKey = key
            });
        }

        [HttpPost("agent")]
        public IActionResult CreateAgentKey()
        {
            var key = ApiKeyService.Generate("agent_live");

            Console.WriteLine($"AGENT API KEY GENERATED: {key}");

            return Ok(new
            {
                apiKey = key
            });
        }
    }
}
