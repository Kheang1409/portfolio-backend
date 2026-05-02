namespace KaiAssistant.Infrastructure.Services;
using global::KaiAssistant.Application.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Resilience;
public sealed class EnhancedResponseCacheService : IResponseCacheService
{
    private readonly ICacheService _cache;
    private readonly ILogger<EnhancedResponseCacheService> _logger;
    // Cache key format: v{version}:hash(normalized_input)
    private const int CacheKeyVersion = 1;
    private const string CacheKeyPrefix = "response";
    public EnhancedResponseCacheService(
        ICacheService cache,
        ILogger<EnhancedResponseCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }
    public string GenerateCacheKey(string userMessage, string? normalizedContext = null)
    {
        // Normalize input: trim, lowercase, remove extra whitespace
        var normalized = NormalizeInput(userMessage);
        // Include context hash if provided (for conversation-specific caching)
        var contextHash = string.IsNullOrEmpty(normalizedContext)
            ? ""
            : $":{HashInput(normalizedContext)}";
        // Create versioned cache key
        var hash = HashInput(normalized);
        var cacheKey = $"{CacheKeyPrefix}:v{CacheKeyVersion}:{hash}{contextHash}";
        _logger.LogDebug(
            "Generated cache key: original_len={Length}, normalized_len={NormLen}, key={Key}",
            userMessage.Length,
            normalized.Length,
            cacheKey[..Math.Min(50, cacheKey.Length)]);
        return cacheKey;
    }
    public async Task<string?> GetCachedResponseAsync(
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        return await _cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);
    }
    public async Task CacheResponseAsync(
        string cacheKey,
        string response,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(cacheKey, response, ttl, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Cached response: key={Key}, ttl={Ttl}s, size={Size}kb",
            cacheKey[..Math.Min(30, cacheKey.Length)],
            ttl.TotalSeconds,
            response.Length / 1024);
    }
    public async Task<(long Hits, long Misses, double HitRate)> GetMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement cache diagnostics tracking
        // For now, return placeholder
        return (0, 0, 0.5);
    }
    private static string NormalizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";
        // 1. Trim and convert to lowercase
        var normalized = input.Trim().ToLowerInvariant();
        // 2. Normalize whitespace (collapse multiple spaces)
        var sb = new StringBuilder();
        bool inSpace = false;
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inSpace)
                {
                    sb.Append(' ');
                    inSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                inSpace = false;
            }
        }
        // 3. Remove punctuation duplicates (e.g., ".." → ".")
        normalized = sb.ToString();
        var result = new StringBuilder();
        char prevChar = '\0';
        foreach (var c in normalized)
        {
            if (char.IsPunctuation(c) && c == prevChar)
                continue;
            result.Append(c);
            prevChar = c;
        }
        return result.ToString();
    }
    private static string HashInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "empty";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)[..12];  // First 12 chars of base64
    }
}
public static class CacheResilienceExtensions
{
    public static IServiceCollection AddEnhancedCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IResponseCacheService>(sp =>
        {
            var baseCache = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<ILogger<EnhancedResponseCacheService>>();
            return new EnhancedResponseCacheService(baseCache, logger);
        });
        return services;
    }
    public static IServiceCollection AddAiResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var policyRegistry = services.AddPolicyRegistry();
        // Register individual policies
        policyRegistry.Add("ai-timeout", AiResiliencePolicies.CreateTimeoutPolicy(TimeSpan.FromSeconds(15)));
        policyRegistry.Add("ai-circuit-breaker", AiResiliencePolicies.CreateCircuitBreakerPolicy());
        policyRegistry.Add("ai-retry", AiResiliencePolicies.CreateRetryPolicy());
        policyRegistry.Add("ai-bulkhead", AiResiliencePolicies.CreateBulkheadPolicy());
        policyRegistry.Add("ai-fallback", AiResiliencePolicies.CreateFallbackPolicy());
        policyRegistry.Add("ai-comprehensive", AiResiliencePolicies.CreateComprehensivePolicy());
        return services;
    }
}