namespace KaiAssistant.Domain.Entities;

public class OpenAiSettings : AssistantBehaviorSettings
{
    public string ApiKey { get; set; } = string.Empty;
}