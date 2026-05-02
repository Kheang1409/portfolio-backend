using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Application.Resumes.Queries;
using KaiAssistant.Application.Resumes.Commands;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
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
    [OutputCache(Duration = 30)]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        var resume = await _mediator.Send(new GetLatestResumeQuery(), cancellationToken).ConfigureAwait(false);
        if (resume == null) return NotFound();
        return Ok(resume);
    }
    [HttpGet("{id}")]
    [OutputCache(Duration = 60)]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        var resume = await _mediator.Send(new GetResumeByIdQuery(id), cancellationToken).ConfigureAwait(false);
        if (resume == null) return NotFound();
        return Ok(resume);
    }
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Resume resume, CancellationToken cancellationToken)
    {
        if (resume == null) return BadRequest();
        var created = await _mediator.Send(new CreateResumeCommand(resume), cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}