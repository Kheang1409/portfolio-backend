namespace KaiAssistant.Application.Interfaces;

public interface IAiPromptBuilder
{
    string BuildSystemPrompt();
    List<object> BuildContents(string resumeContext, string question);
    object BuildGenerationConfig(string question);
}
