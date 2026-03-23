using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaiAssistant.API.Services;

public sealed class OperationalSimulationState : IOperationalSimulationState
{
    private const string AiThrottleKey = "sim:ai:throttle:until";
    private const string OutboxDelayKey = "sim:outbox:delay:ms";

    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;

    public OperationalSimulationState(IServiceProvider serviceProvider, IMemoryCache memoryCache)
    {
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _memoryCache = memoryCache;
    }

    public DateTimeOffset? ForceAiThrottleUntilUtc
    {
        get
        {
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                var value = db.StringGet(AiThrottleKey);
                if (!value.HasValue)
                {
                    return null;
                }

                return DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed : null;
            }

            return _memoryCache.Get<DateTimeOffset?>(AiThrottleKey);
        }
    }

    public int OutboxArtificialDelayMs
    {
        get
        {
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                var value = db.StringGet(OutboxDelayKey);
                return value.HasValue && int.TryParse(value.ToString(), out var parsed)
                    ? Math.Clamp(parsed, 0, 15_000)
                    : 0;
            }

            return _memoryCache.Get<int?>(OutboxDelayKey) ?? 0;
        }
    }

    public void ForceAiThrottleFor(TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow.Add(duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            db.StringSet(AiThrottleKey, until.ToString("O"), expiry: TimeSpan.FromHours(1));
            return;
        }

        _memoryCache.Set(AiThrottleKey, until, TimeSpan.FromHours(1));
    }

    public void SetOutboxArtificialDelay(int delayMs)
    {
        var bounded = Math.Clamp(delayMs, 0, 15_000);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            db.StringSet(OutboxDelayKey, bounded.ToString(), expiry: TimeSpan.FromHours(1));
            return;
        }

        _memoryCache.Set(OutboxDelayKey, bounded, TimeSpan.FromHours(1));
    }
}
