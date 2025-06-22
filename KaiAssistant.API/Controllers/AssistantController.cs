using Microsoft.AspNetCore.Mvc;
using MediatR;
using KaiAssistant.Application.AskAssistants.Commands;

namespace KaiAssistant.API.Controller;

[ApiController]
[Route("/api/assistants")]
public class AssistantController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssistantController(IMediator mediator)
    {
        _mediator = mediator;
    }
    [HttpPost("ask")]
    public async Task<IActionResult> Applied([FromBody] AskAssistantCommand command)
    {
        var response = await _mediator.Send(command);
        return Ok(response);
    }

}