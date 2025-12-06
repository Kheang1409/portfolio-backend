namespace KaiAssistant.Domain.Entities;

public class GeminiSettings : AssistantBehaviorSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public List<string> ModelNames { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;
    public int PromptMaxChars { get; set; } = 10000;
    public bool IncludePersonalDetails { get; set; } = true;
    public double? Temperature { get; set; }
    public int? TopK { get; set; }
    public double? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? CandidateCount { get; set; }
}