using KaiAssistant.Application.Diagnostics;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaiAssistant.Infrastructure.Observability;

public sealed class ResilienceStatusProvider : IResilienceStatusProvider
{
    private static readonly string[] Components = ["ai", "rabbitmq", "email"];
    private readonly IConnectionMultiplexer? _redis;
    private readonly Dictionary<string, ResilienceComponentStatus> _fallbackState = new(StringComparer.OrdinalIgnoreCase);

    public ResilienceStatusProvider(IServiceProvider serviceProvider)
    {
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        foreach (var component in Components)
        {
            _fallbackState[component] = Create(component);
        }
    }

    public ResilienceSnapshot GetSnapshot()
    {
        if (_redis is null)
        {
            var fallback = _fallbackState.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ResilienceComponentStatus
                {
                    Name = x.Name,
                    CircuitState = x.CircuitState,
                    FailureCount = x.FailureCount,
                    LastFailureAtUtc = x.LastFailureAtUtc,
                    LastFailureReason = x.LastFailureReason
                })
                .ToList();

            return new ResilienceSnapshot { Components = fallback };
        }

        var db = _redis.GetDatabase();
        var list = Components
            .Select(component =>
            {
                var key = BuildKey(component);
                var values = db.HashGetAll(key);
                var status = Create(component);

                foreach (var value in values)
                {
                    if (value.Name == "circuitState")
                    {
                        status.CircuitState = value.Value.ToString();
                        continue;
                    }

                    if (value.Name == "failureCount" && long.TryParse(value.Value.ToString(), out var failureCount))
                    {
                        status.FailureCount = failureCount;
                        continue;
                    }

                    if (value.Name == "lastFailureAtUtc" && DateTimeOffset.TryParse(value.Value.ToString(), out var lastFailureAtUtc))
                    {
                        status.LastFailureAtUtc = lastFailureAtUtc;
                        continue;
                    }

                    if (value.Name == "lastFailureReason")
                    {
                        status.LastFailureReason = value.Value.ToString();
                    }
                }

                return status;
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResilienceSnapshot { Components = list };
    }

    public void RecordFailure(string component, string reason)
    {
        var safeComponent = Normalize(component);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(safeComponent);
            var now = DateTimeOffset.UtcNow;
            db.HashIncrement(key, "failureCount", 1);
            db.HashSet(key, [
                new HashEntry("name", safeComponent),
                new HashEntry("lastFailureAtUtc", now.ToString("O")),
                new HashEntry("lastFailureReason", reason ?? string.Empty)
            ]);
            db.KeyExpire(key, TimeSpan.FromDays(7));
            return;
        }

        var status = _fallbackState.TryGetValue(safeComponent, out var existing) ? existing : Create(safeComponent);
        status.FailureCount++;
        status.LastFailureAtUtc = DateTimeOffset.UtcNow;
        status.LastFailureReason = reason;
        _fallbackState[safeComponent] = status;
    }

    public void RecordSuccess(string component)
    {
        RecordCircuitState(component, "Closed");
    }

    public void RecordCircuitState(string component, string state)
    {
        var safeComponent = Normalize(component);
        var safeState = string.IsNullOrWhiteSpace(state) ? "Closed" : state;
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(safeComponent);
            db.HashSet(key, [
                new HashEntry("name", safeComponent),
                new HashEntry("circuitState", safeState)
            ]);
            db.KeyExpire(key, TimeSpan.FromDays(7));
            return;
        }

        var status = _fallbackState.TryGetValue(safeComponent, out var existing) ? existing : Create(safeComponent);
        status.CircuitState = safeState;
        _fallbackState[safeComponent] = status;
    }

    private static ResilienceComponentStatus Create(string name)
    {
        return new ResilienceComponentStatus
        {
            Name = name,
            CircuitState = "Closed"
        };
    }

    private static string Normalize(string component)
    {
        return string.IsNullOrWhiteSpace(component) ? "unknown" : component.Trim().ToLowerInvariant();
    }

    private static string BuildKey(string component)
    {
        return $"resilience:component:{component}";
    }
}
