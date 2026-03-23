namespace KaiAssistant.Application.Interfaces;

public interface IFeatureFlagService
{
    bool EnableRabbitMqPublishing { get; }
    bool EnableOutboxProcessing { get; }
    bool EnableOutboxRecovery { get; }
    bool EnableAiResponseCache { get; }
    bool EnableAssistantBatching { get; }
    bool EnableCache { get; }
    bool EnableRateLimiting { get; }
    bool EnableStreaming { get; }
}
