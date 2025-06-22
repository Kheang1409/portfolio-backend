using Microsoft.AspNetCore.Mvc;

namespace ContactFormApi.Controllers
{
    [ApiController]
    [Route("api/healths")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });
        }
    }
}