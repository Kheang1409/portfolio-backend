using Microsoft.AspNetCore.Mvc;

namespace ContactFormApi.Controllers
{
    [ApiController]
    [Route("api/healths")]
    public class HealthController : ControllerBase
    {
        [HttpHead]
        public IActionResult Get()
        {
            return Ok();
        }
    }
}