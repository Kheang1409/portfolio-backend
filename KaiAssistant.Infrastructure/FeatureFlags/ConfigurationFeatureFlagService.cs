using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace KaiAssistant.Infrastructure.FeatureFlags;

public sealed class ConfigurationFeatureFlagService : IFeatureFlagService
{
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;
    private readonly ILogger<ConfigurationFeatureFlagService> _logger;

    public ConfigurationFeatureFlagService(IOptionsMonitor<FeatureFlagsOptions> options, ILogger<ConfigurationFeatureFlagService> logger)
    {
        _options = options;
        _logger = logger;

        _options.OnChange(flags =>
        {
            _logger.LogInformation(
                "AuditFeatureFlagsChanged: rabbit={Rabbit} outbox={Outbox} outboxRecovery={OutboxRecovery} aiCache={AiCache} assistantBatching={AssistantBatching} cache={Cache} rateLimit={RateLimit} streaming={Streaming}",
                flags.EnableRabbitMqPublishing,
                flags.EnableOutboxProcessing,
                flags.EnableOutboxRecovery,
                flags.EnableAiResponseCache,
                flags.EnableAssistantBatching,
                flags.EnableCache,
                flags.EnableRateLimiting,
                flags.EnableStreaming);
        });
    }

    public bool EnableRabbitMqPublishing => _options.CurrentValue.EnableRabbitMqPublishing;
    public bool EnableOutboxProcessing => _options.CurrentValue.EnableOutboxProcessing;
    public bool EnableOutboxRecovery => _options.CurrentValue.EnableOutboxRecovery;
    public bool EnableAiResponseCache => _options.CurrentValue.EnableAiResponseCache;
    public bool EnableAssistantBatching => _options.CurrentValue.EnableAssistantBatching;
    public bool EnableCache => _options.CurrentValue.EnableCache;
    public bool EnableRateLimiting => _options.CurrentValue.EnableRateLimiting;
    public bool EnableStreaming => _options.CurrentValue.EnableStreaming;
}
