using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Diagnostics;
using MediatR;
using System.Text.RegularExpressions;

namespace KaiAssistant.Application.AskAssistants.Commands;

public class AskAssistantCommandHandler : IRequestHandler<AskAssistantCommand, AiResponse>
{
    private readonly IAssistantService _service;
    private readonly IAiUsageGuard _usageGuard;

    public AskAssistantCommandHandler(IAssistantService service, IAiUsageGuard usageGuard)
    {
        _service = service;
        _usageGuard = usageGuard;
    }

    public async Task<AiResponse> Handle(AskAssistantCommand command, CancellationToken cancellationToken)
    {
        var decision = await _usageGuard
            .EvaluateAsync(command.Question, command.History, command.Context, cancellationToken)
            .ConfigureAwait(false);

        if (!decision.Allowed)
        {
            return new AiResponse { Text = decision.UserFacingMessage };
        }

        if (!_usageGuard.TryChargeBudget(decision.EstimatedCostUsd, command.Context, out var rejectionMessage))
        {
            return new AiResponse
            {
                Text = rejectionMessage ?? "AI request budget exceeded. Please try again later.",
                EstimatedCostUsd = decision.EstimatedCostUsd
            };
        }

        var response = await _service
            .AskQuestionAsync(decision.Question, decision.History, command.Context, cancellationToken)
            .ConfigureAwait(false);

        var quality = _usageGuard.EvaluateResponse(response.Text);
        if (!quality.Allowed)
        {
            response.Text = quality.UserFacingMessage;
        }
        else
        {
            response.Text = quality.NormalizedResponse;
        }

        var outputTokens = EstimateTokens(response.Text);
        _usageGuard.RecordTokensUsed(decision.EstimatedInputTokens, outputTokens);
        return response;
    }

    private static int EstimateTokens(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        var withoutControls = Regex.Replace(input, "[\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F]", string.Empty);
        var normalized = Regex.Replace(withoutControls, "\\s+", " ").Trim();
        return (int)Math.Ceiling(normalized.Length / 4d);
    }
}