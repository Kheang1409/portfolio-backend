namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    void LoadResume(string filePath);
    void LoadResumeFromDatabase();
    Task LoadResumeFromDatabaseAsync(CancellationToken cancellationToken = default);
    Task<string> AskQuestionAsync(string question);
}
