namespace KaiAssistant.Application.Interfaces;
public interface IPromptSecurityService
{
    string SanitizeInput(string input);
    string SanitizeOutput(string output);
    bool LooksLikePromptInjection(string input);
}