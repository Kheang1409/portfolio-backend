using System.Security.Cryptography;
using System.Text;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.Cache;
using StackExchange.Redis;
namespace KaiAssistant.Infrastructure.Services;
public class ResponseCacheService : IResponseCacheService
{
    private readonly ICacheService _cache;
    private readonly IRedisConnectionFactory _redisFactory;
    private const string RESPONSE_PREFIX = "ai:response:";
    private const string HITS_KEY = "cache:hits";
    private const string MISSES_KEY = "cache:misses";
    public ResponseCacheService(ICacheService cache, IRedisConnectionFactory redisFactory)
    {
        _cache = cache;
        _redisFactory = redisFactory;
    }
    public string GenerateCacheKey(string userMessage, string? normalizedContext = null)
    {
        var combined = normalizedContext != null
            ? $"{userMessage}:{normalizedContext}"
            : userMessage;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return $"{RESPONSE_PREFIX}{Convert.ToHexString(hash).ToLower()[..16]}";
    }
    public async Task<string?> GetCachedResponseAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);
        var redis = await _redisFactory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (cached != null)
        {
            if (redis is not null)
            {
                var db = redis.GetDatabase();
                await db.StringIncrementAsync(HITS_KEY).ConfigureAwait(false);
            }
        }
        else
        {
            if (redis is not null)
            {
                var db = redis.GetDatabase();
                await db.StringIncrementAsync(MISSES_KEY).ConfigureAwait(false);
            }
        }
        return cached;
    }
    public async Task CacheResponseAsync(
        string cacheKey,
        string response,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(cacheKey, response, ttl, cancellationToken).ConfigureAwait(false);
    }
    public async Task<(long Hits, long Misses, double HitRate)> GetMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var redis = await _redisFactory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (redis is null)
        {
            return (0, 0, 0);
        }
        var db = redis.GetDatabase();
        var hitsVal = await db.StringGetAsync(HITS_KEY).ConfigureAwait(false);
        var missesVal = await db.StringGetAsync(MISSES_KEY).ConfigureAwait(false);
        var hits = hitsVal.IsNullOrEmpty ? 0 : long.Parse(hitsVal.ToString());
        var misses = missesVal.IsNullOrEmpty ? 0 : long.Parse(missesVal.ToString());
        var total = hits + misses;
        var hitRate = total == 0 ? 0.0 : (double)hits / total;
        return (hits, misses, hitRate);
    }
}
