namespace KaiAssistant.Application.Interfaces;
using global::KaiAssistant.Application.DTOs;
public interface IEnhancedAssistantOrchestrator : IAssistantOrchestrator
{
    IAsyncEnumerable<KaiAssistant.Application.DTOs.AssistantStreamEvent> OrchestrateWithEnhancedStreamingAsync(
        string userMessage,
        string conversationId,
        string? userId = null,
        CancellationToken cancellationToken = default);
    Task<object> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> arguments,
        CancellationToken cancellationToken = default);
}