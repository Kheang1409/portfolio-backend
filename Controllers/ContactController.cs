using ContactFormApi.DTOs;
using ContactFormApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactFormApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
    private readonly IEmailService _emailService;

    public ContactController(IEmailService emailService)
    {
        _emailService = emailService;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ContactFormDto contact)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await _emailService.SendContactEmailAsync(contact);
            return Ok(new { Message = "Email sent successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "Failed to send email.", Error = ex.Message });
        }
    }
}
