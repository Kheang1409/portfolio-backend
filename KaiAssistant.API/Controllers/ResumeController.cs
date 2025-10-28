using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Application.Resumes.Queries;
using KaiAssistant.Application.Resumes.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KaiAssistant.API.Controllers;

[ApiController]
[Route("api/resumes")]
public class ResumeController : ControllerBase
{
    private readonly IMediator _mediator;

    public ResumeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest()
    {
        var resume = await _mediator.Send(new GetLatestResumeQuery());
        if (resume == null) return NotFound();
        return Ok(resume);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var resume = await _mediator.Send(new GetResumeByIdQuery(id));
        if (resume == null) return NotFound();
        return Ok(resume);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Resume resume)
    {
        if (resume == null) return BadRequest();
        var created = await _mediator.Send(new CreateResumeCommand(resume));
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
