namespace KaiAssistant.Domain.Entities;

public class GeminiSettings : AssistantBehaviorSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}