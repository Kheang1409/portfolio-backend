using Microsoft.AspNetCore.Mvc;
using MediatR;
using KaiAssistant.Application.AskAssistants.Commands;
using KaiAssistant.Application.DTOs;

namespace KaiAssistant.API.Controllers;

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
    public async Task<IActionResult> Applied([FromBody] TextDto dto)
    {
        var command = new AskAssistantCommand(dto.Message);
        var response = await _mediator.Send(command);
        return Ok(response);
    }
}