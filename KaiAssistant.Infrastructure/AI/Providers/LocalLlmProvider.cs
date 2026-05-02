using System.Diagnostics;
using KaiAssistant.Application.AI;
using KaiAssistant.Application.Interfaces;
namespace KaiAssistant.Infrastructure.AI.Providers;
public sealed class LocalLlmProvider : IAiProvider
{
    public string ProviderName => "local";
    public Task<AiResponse> GenerateAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var content = "Local LLM provider stub is enabled, but no local model endpoint is configured.";
        sw.Stop();
        return Task.FromResult(new AiResponse
        {
            Content = content,
            ModelUsed = "local-stub",
            LatencyMs = (long)sw.Elapsed.TotalMilliseconds,
            FallbackUsed = false,
            InputTokens = (int)Math.Ceiling(request.Prompt.Length / 4d),
            OutputTokens = (int)Math.Ceiling(content.Length / 4d)
        });
    }
}