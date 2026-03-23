using FluentAssertions;
using KaiAssistant.API.Controllers;
using KaiAssistant.Application.Diagnostics;
using KaiAssistant.Application.DTOs;
using KaiAssistant.Application.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KaiAssistant.Tests;

#nullable enable

public class AssistantStreamingControllerTests
{
    [Fact]
    public async Task Stream_WhenEnabled_WritesNdjsonChunks()
    {
        var mediator = new Mock<IMediator>();
        var flags = new Mock<IFeatureFlagService>();
        var assistant = new Mock<IAssistantService>();

        flags.SetupGet(x => x.EnableStreaming).Returns(true);
        assistant
            .Setup(x => x.StreamQuestionAsync(It.IsAny<string>(), It.IsAny<KaiAssistant.Domain.Entities.ConversationMessage[]?>(), It.IsAny<KaiAssistant.Domain.Entities.AssistantContext?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateStreamChunks());

        var controller = new AssistantController(mediator.Object, flags.Object, assistant.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.Stream(new TextDto("hello"), CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        httpContext.Response.ContentType.Should().Be("application/x-ndjson");

        httpContext.Response.Body.Position = 0;
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"type\":\"delta\"");
        body.Should().Contain("\"type\":\"completed\"");
    }

    [Fact]
    public async Task Stream_WhenCancelled_DoesNotThrow()
    {
        var mediator = new Mock<IMediator>();
        var flags = new Mock<IFeatureFlagService>();
        var assistant = new Mock<IAssistantService>();

        flags.SetupGet(x => x.EnableStreaming).Returns(true);
        assistant
            .Setup(x => x.StreamQuestionAsync(It.IsAny<string>(), It.IsAny<KaiAssistant.Domain.Entities.ConversationMessage[]?>(), It.IsAny<KaiAssistant.Domain.Entities.AssistantContext?>(), It.IsAny<CancellationToken>()))
            .Returns(CancelledStream());

        var controller = new AssistantController(mediator.Object, flags.Object, assistant.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await controller.Stream(new TextDto("cancel"), cts.Token);
        await act.Should().NotThrowAsync();
    }

    private static async IAsyncEnumerable<AiStreamChunk> CreateStreamChunks()
    {
        yield return new AiStreamChunk
        {
            Type = "delta",
            MessageId = "m1",
            Text = "Hello",
            ModelUsed = "gemini-2.5-flash:generateContent"
        };

        await Task.Yield();

        yield return new AiStreamChunk
        {
            Type = "completed",
            MessageId = "m1",
            LatencyMs = 120,
            TtftMs = 30,
            ThroughputTokensPerSecond = 12.5
        };
    }

    private static async IAsyncEnumerable<AiStreamChunk> CancelledStream()
    {
        await Task.Delay(1);
        throw new OperationCanceledException();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
