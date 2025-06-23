namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    void LoadResume(string filePath);
    Task<string> AskQuestionAsync(string question);
}
