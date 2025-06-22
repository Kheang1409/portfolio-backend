namespace KaiAssistant.Application.Interfaces;

public interface IAssistantService
{
    Task<string> AskQuestionAsync(string question);
    void LoadResume(string filePath);
}
