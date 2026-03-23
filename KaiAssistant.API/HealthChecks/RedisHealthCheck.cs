using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaiAssistant.API.HealthChecks;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IFeatureFlagService _flags;

    public RedisHealthCheck(IServiceProvider serviceProvider, IFeatureFlagService flags)
    {
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _flags = flags;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_flags.EnableCache)
        {
            return HealthCheckResult.Healthy("Redis not required because cache feature is disabled.");
        }

        if (_redis is null)
        {
            return HealthCheckResult.Degraded("Redis connection is not configured; fallback cache may be used.");
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync().ConfigureAwait(false);
            return _redis.IsConnected
                ? HealthCheckResult.Healthy("Redis is healthy.")
                : HealthCheckResult.Degraded("Redis multiplexer is disconnected.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connectivity check failed.", ex);
        }
    }
}
