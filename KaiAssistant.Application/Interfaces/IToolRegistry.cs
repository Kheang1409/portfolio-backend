namespace KaiAssistant.Application.Interfaces;
public interface IToolRegistry
{
    IAssistantTool? GetTool(string toolName);
    IEnumerable<IAssistantTool> GetAllTools();
    bool HasTool(string toolName);
    string GetFormattedToolManifest();
}