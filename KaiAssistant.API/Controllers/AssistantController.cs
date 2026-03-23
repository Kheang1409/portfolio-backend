using Microsoft.AspNetCore.Mvc;
using MediatR;
using KaiAssistant.Application.AskAssistants.Commands;
using KaiAssistant.Application.DTOs;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaiAssistant.API.Controllers;

[ApiController]
[Route("/api/assistants")]
public class AssistantController : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IMediator _mediator;
    private readonly IFeatureFlagService _flags;
    private readonly IAssistantService _assistantService;

    public AssistantController(IMediator mediator, IFeatureFlagService flags, IAssistantService assistantService)
    {
        _mediator = mediator;
        _flags = flags;
        _assistantService = assistantService;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Applied([FromBody] TextDto dto, CancellationToken cancellationToken)
    {
        var command = new AskAssistantCommand(dto.Message, dto.History, dto.Context?.ToDomain());
        var response = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(response.ModelUsed))
        {
            Response.Headers["X-AI-Model-Used"] = response.ModelUsed;
        }

        Response.Headers["X-AI-Latency-Ms"] = response.LatencyMs.ToString("F0");
        Response.Headers["X-AI-Fallback-Used"] = response.FallbackUsed ? "true" : "false";
        return Ok(response.Text);
    }

    [HttpPost("ask/batch")]
    public async Task<IActionResult> AskBatch([FromBody] BatchTextDto dto, CancellationToken cancellationToken)
    {
        if (!_flags.EnableAssistantBatching)
        {
            return NotFound();
        }

        if (dto.Items is null || dto.Items.Length == 0)
        {
            return BadRequest(new { message = "At least one message is required." });
        }

        var batchSize = Math.Min(dto.Items.Length, 10);
        var results = new List<string>(batchSize);

        for (var i = 0; i < batchSize; i++)
        {
            var item = dto.Items[i];
            var command = new AskAssistantCommand(item.Message, item.History, item.Context?.ToDomain());
            var response = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
            results.Add(response.Text);
        }

        return Ok(new
        {
            count = results.Count,
            responses = results
        });
    }

    [HttpPost("stream")]
    public async Task Stream([FromBody] TextDto dto, CancellationToken cancellationToken)
    {
        if (!_flags.EnableStreaming)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            await Response.WriteAsJsonAsync(new
            {
                errorCode = "STREAMING_DISABLED",
                retryable = false,
                message = "Streaming is disabled. Use /api/assistants/ask instead."
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.CacheControl = "no-cache";

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);

        try
        {
            await foreach (var chunk in _assistantService
                               .StreamQuestionAsync(dto.Message, dto.History, dto.Context?.ToDomain(), linkedCts.Token)
                               .ConfigureAwait(false))
            {
                await WriteStreamLineAsync(chunk, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // Client disconnected or cancelled request; stop quietly.
        }
        catch (Exception)
        {
            await WriteStreamLineAsync(new AiStreamChunk
            {
                Type = "error",
                MessageId = Guid.NewGuid().ToString("N"),
                ErrorCode = "STREAM_UNHANDLED",
                Retryable = true,
                ErrorMessage = "Streaming failed due to an internal error."
            }, linkedCts.Token).ConfigureAwait(false);
        }
    }

    private async Task WriteStreamLineAsync(AiStreamChunk chunk, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(Response.Body, chunk, StreamJsonOptions, cancellationToken).ConfigureAwait(false);
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("\n"), cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}