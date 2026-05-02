using System.Net.Sockets;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.EventBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
namespace KaiAssistant.API.HealthChecks;
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IFeatureFlagService _flags;
    private readonly IOptionsMonitor<RabbitMqOptions> _options;
    public RabbitMqHealthCheck(IFeatureFlagService flags, IOptionsMonitor<RabbitMqOptions> options)
    {
        _flags = flags;
        _options = options;
    }
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_flags.EnableRabbitMqPublishing)
        {
            return HealthCheckResult.Healthy("RabbitMQ publishing is disabled by feature flag.");
        }
        var rabbit = _options.CurrentValue;
        if (!rabbit.Enabled || string.IsNullOrWhiteSpace(rabbit.HostName))
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is enabled by feature flag but broker configuration is invalid.");
        }
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(rabbit.HostName, rabbit.Port, timeoutCts.Token).ConfigureAwait(false);
            return client.Connected
                ? HealthCheckResult.Healthy("RabbitMQ TCP connectivity is healthy.")
                : HealthCheckResult.Unhealthy("RabbitMQ TCP connectivity failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ connectivity check failed.", ex);
        }
    }
}