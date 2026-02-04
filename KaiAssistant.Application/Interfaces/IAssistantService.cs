using KaiAssistant.Domain.Entities;

namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    Task<string> AskQuestionAsync(string question, ConversationMessage[]? history = null, CancellationToken cancellationToken = default);
}
