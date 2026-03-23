using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Diagnostics;

namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    Task<AiResponse> AskQuestionAsync(
        string question,
        ConversationMessage[]? history = null,
        AssistantContext? context = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AiStreamChunk> StreamQuestionAsync(
        string question,
        ConversationMessage[]? history = null,
        AssistantContext? context = null,
        CancellationToken cancellationToken = default);
}
