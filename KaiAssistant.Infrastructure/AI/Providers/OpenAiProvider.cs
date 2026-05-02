using KaiAssistant.Application.AI;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
namespace KaiAssistant.Infrastructure.AI.Providers;
public sealed class OpenAiProvider : IAiProvider
{
    private readonly ILogger<OpenAiProvider> _logger;
    public OpenAiProvider(ILogger<OpenAiProvider> logger)
    {
        _logger = logger;
    }
    public string ProviderName => "openai";
    public Task<AiResponse> GenerateAsync(AiRequest request, CancellationToken cancellationToken)
    {
        _logger.LogWarning("OpenAI provider is configured as a fallback stub and is currently not enabled. Returning graceful fallback content.");
        return Task.FromResult(new AiResponse
        {
            Content = "I'm having trouble reaching the primary AI provider right now. Please try again in a moment, or ask a shorter question while the service stabilizes.",
            ModelUsed = "openai-stub",
            LatencyMs = 0,
            FallbackUsed = true
        });
    }
}