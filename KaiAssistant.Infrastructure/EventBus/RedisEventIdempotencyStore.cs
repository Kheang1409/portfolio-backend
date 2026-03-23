using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaiAssistant.Infrastructure.EventBus;

public sealed class RedisEventIdempotencyStore : IEventIdempotencyStore
{
    private const string ProcessedValue = "processed";
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;

    public RedisEventIdempotencyStore(IServiceProvider serviceProvider, IMemoryCache memoryCache)
    {
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _memoryCache = memoryCache;
    }

    public async Task<IdempotencyAcquireResult> TryAcquireAsync(string idempotencyKey, TimeSpan processingTtl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return IdempotencyAcquireResult.Acquired;
        }

        var key = BuildKey(idempotencyKey);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var existing = await db.StringGetAsync(key).ConfigureAwait(false);
            if (existing.HasValue && string.Equals(existing.ToString(), ProcessedValue, StringComparison.Ordinal))
            {
                return IdempotencyAcquireResult.AlreadyProcessed;
            }

            var lockValue = $"processing:{Environment.MachineName}:{Guid.NewGuid():N}";
            var acquired = await db.StringSetAsync(key, lockValue, processingTtl, When.NotExists).ConfigureAwait(false);
            if (acquired)
            {
                return IdempotencyAcquireResult.Acquired;
            }

            var current = await db.StringGetAsync(key).ConfigureAwait(false);
            if (current.HasValue && string.Equals(current.ToString(), ProcessedValue, StringComparison.Ordinal))
            {
                return IdempotencyAcquireResult.AlreadyProcessed;
            }

            return IdempotencyAcquireResult.Busy;
        }

        if (_memoryCache.TryGetValue<string>(key, out var status) && string.Equals(status, ProcessedValue, StringComparison.Ordinal))
        {
            return IdempotencyAcquireResult.AlreadyProcessed;
        }

        if (_memoryCache.TryGetValue<string>(key, out _))
        {
            return IdempotencyAcquireResult.Busy;
        }

        _memoryCache.Set(key, "processing", processingTtl);
        return IdempotencyAcquireResult.Acquired;
    }

    public async Task MarkProcessedAsync(string idempotencyKey, TimeSpan processedTtl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var key = BuildKey(idempotencyKey);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, ProcessedValue, processedTtl).ConfigureAwait(false);
            return;
        }

        _memoryCache.Set(key, ProcessedValue, processedTtl);
    }

    public async Task<bool> IsProcessedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return false;
        }

        var key = BuildKey(idempotencyKey);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var val = await db.StringGetAsync(key).ConfigureAwait(false);
            return val.HasValue && string.Equals(val.ToString(), ProcessedValue, StringComparison.Ordinal);
        }

        return _memoryCache.TryGetValue<string>(key, out var status)
            && string.Equals(status, ProcessedValue, StringComparison.Ordinal);
    }

    public async Task ReleaseAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var key = BuildKey(idempotencyKey);
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            var current = await db.StringGetAsync(key).ConfigureAwait(false);
            if (current.HasValue && !string.Equals(current.ToString(), ProcessedValue, StringComparison.Ordinal))
            {
                await db.KeyDeleteAsync(key).ConfigureAwait(false);
            }

            return;
        }

        if (_memoryCache.TryGetValue<string>(key, out var status) && !string.Equals(status, ProcessedValue, StringComparison.Ordinal))
        {
            _memoryCache.Remove(key);
        }
    }

    private static string BuildKey(string idempotencyKey)
    {
        return $"idem:event:{idempotencyKey}";
    }
}
