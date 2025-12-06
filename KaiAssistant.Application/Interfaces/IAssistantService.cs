namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    Task<string> AskQuestionAsync(string question, CancellationToken cancellationToken = default);
}
