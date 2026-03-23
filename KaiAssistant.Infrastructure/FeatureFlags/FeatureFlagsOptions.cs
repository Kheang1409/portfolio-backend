namespace KaiAssistant.Infrastructure.FeatureFlags;

public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    public bool EnableRabbitMqPublishing { get; set; } = true;
    public bool EnableOutboxProcessing { get; set; } = true;
    public bool EnableOutboxRecovery { get; set; } = false;
    public bool EnableAiResponseCache { get; set; } = false;
    public bool EnableAssistantBatching { get; set; } = false;
    public bool EnableCache { get; set; } = true;
    public bool EnableRateLimiting { get; set; } = true;
    public bool EnableStreaming { get; set; } = true;
}
