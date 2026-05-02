namespace KaiAssistant.Application.Interfaces;
public interface IResponseCacheService
{
    string GenerateCacheKey(string userMessage, string? normalizedContext = null);
    Task<string?> GetCachedResponseAsync(string cacheKey, CancellationToken cancellationToken = default);
    Task CacheResponseAsync(
        string cacheKey,
        string response,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
    Task<(long Hits, long Misses, double HitRate)> GetMetricsAsync(CancellationToken cancellationToken = default);
}