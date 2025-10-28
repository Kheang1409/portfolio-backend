using KaiAssistant.Application.Contacts.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KaiAssistant.API.Controllers;

[ApiController]
[Route("api/contacts")]
public class ContactController : ControllerBase
{
    private readonly IMediator _mediator;

    public ContactController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ContactCommand command)
    {
        if (command == null)
            return BadRequest(ModelState);
        await _mediator.Send(command);
        return Ok(new { Message = "Email sent successfully." });
    }
}
