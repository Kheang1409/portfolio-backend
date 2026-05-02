namespace KaiAssistant.Application.Interfaces;
public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }
    string InputSchema { get; }
    Task<object> ExecuteAsync(Dictionary<string, object> args, CancellationToken cancellationToken = default);
    bool ValidateArguments(Dictionary<string, object> args);
}