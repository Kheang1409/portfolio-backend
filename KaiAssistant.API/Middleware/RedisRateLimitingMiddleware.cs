using KaiAssistant.API.Options;
using KaiAssistant.API.Services;
using KaiAssistant.Application.Interfaces;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KaiAssistant.API.Middleware;

public sealed class RedisRateLimitingMiddleware
{
    private static readonly Meter Meter = new("KaiAssistant.RateLimiting", "1.0.0");
    private static readonly Counter<long> AllowedRequests = Meter.CreateCounter<long>("rate_limit_allowed_total");
    private static readonly Counter<long> BlockedRequests = Meter.CreateCounter<long>("rate_limit_blocked_total");

        private const string SlidingWindowScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local permit = tonumber(ARGV[3])
local member = ARGV[4]

redis.call('ZREMRANGEBYSCORE', key, '-inf', now - window)
local count = redis.call('ZCARD', key)

if count >= permit then
    local first = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
    local retryAfter = 1
    if first[2] ~= nil then
        retryAfter = math.max(1, math.floor((tonumber(first[2]) + window - now + 999) / 1000))
    end
    return {0, retryAfter}
end

redis.call('ZADD', key, now, member)
redis.call('EXPIRE', key, math.max(1, math.floor(window / 1000)))
return {1, 0}
";

    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RedisRateLimitingMiddleware> _logger;
    private readonly RateLimitingOptions _options;
    private readonly IFeatureFlagService _flags;
    private readonly IRateLimitTelemetry _telemetry;

    public RedisRateLimitingMiddleware(
        RequestDelegate next,
        IServiceProvider serviceProvider,
        IMemoryCache memoryCache,
        IOptions<RateLimitingOptions> options,
        IFeatureFlagService flags,
        IRateLimitTelemetry telemetry,
        ILogger<RedisRateLimitingMiddleware> logger)
    {
        _next = next;
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _memoryCache = memoryCache;
        _logger = logger;
        _options = options.Value;
        _flags = flags;
        _telemetry = telemetry;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_flags.EnableRateLimiting)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!ShouldApply(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var (allowed, retryAfterSeconds, clientIp) = await IsAllowedAsync(context).ConfigureAwait(false);
        if (!allowed)
        {
            _telemetry.RecordBlocked(clientIp, context.Request.Path.Value ?? "unknown");
            BlockedRequests.Add(1,
                KeyValuePair.Create<string, object?>("path", context.Request.Path.Value ?? "unknown"),
                KeyValuePair.Create<string, object?>("window_seconds", _options.WindowSeconds));
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Rate limit exceeded. Please retry later."
            }).ConfigureAwait(false);
            return;
        }

        _telemetry.RecordAllowed(clientIp, context.Request.Path.Value ?? "unknown");
        AllowedRequests.Add(1,
            KeyValuePair.Create<string, object?>("path", context.Request.Path.Value ?? "unknown"),
            KeyValuePair.Create<string, object?>("window_seconds", _options.WindowSeconds));

        await _next(context).ConfigureAwait(false);
    }

    private bool ShouldApply(PathString path)
    {
        foreach (var prefix in _options.PathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(bool Allowed, int RetryAfterSeconds, string ClientIp)> IsAllowedAsync(HttpContext context)
    {
        var permitLimit = Math.Max(1, _options.PermitLimit);
        var burstMultiplier = Math.Max(1, _options.BurstMultiplier);
        permitLimit *= burstMultiplier;
        var windowSeconds = Math.Max(1, _options.WindowSeconds);

        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        var clientIp = !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded.Split(',')[0].Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;
        var key = _options.UseSlidingWindow
            ? $"ratelimit:sliding:{clientIp}"
            : $"ratelimit:{clientIp}:{window}";

        if (_redis is not null)
        {
            var db = _redis.GetDatabase();

            var globalPermit = _options.GlobalPermitLimit.GetValueOrDefault(0);
            if (globalPermit > 0)
            {
                var globalKey = _options.UseSlidingWindow
                    ? "ratelimit:sliding:global"
                    : $"ratelimit:global:{window}";

                if (_options.UseSlidingWindow)
                {
                    var windowMsGlobal = windowSeconds * 1000L;
                    var nowMsGlobal = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var memberGlobal = $"{nowMsGlobal}:{Guid.NewGuid():N}";
                    var globalResult = await db
                        .ScriptEvaluateAsync(
                            SlidingWindowScript,
                            [new RedisKey(globalKey)],
                            [nowMsGlobal, windowMsGlobal, globalPermit, memberGlobal])
                        .ConfigureAwait(false);

                    var globalTuple = (RedisResult[]?)globalResult;
                    if (globalTuple is not null && globalTuple.Length >= 2 && (long)globalTuple[0] == 0)
                    {
                        return (false, (int)(long)globalTuple[1], clientIp);
                    }
                }
                else
                {
                    var globalCount = await db.StringIncrementAsync(globalKey).ConfigureAwait(false);
                    if (globalCount == 1)
                    {
                        await db.KeyExpireAsync(globalKey, TimeSpan.FromSeconds(windowSeconds)).ConfigureAwait(false);
                    }

                    if (globalCount > globalPermit)
                    {
                        return (false, windowSeconds, clientIp);
                    }
                }
            }

            if (_options.UseSlidingWindow)
            {
                var windowMs = windowSeconds * 1000L;
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var member = $"{nowMs}:{Guid.NewGuid():N}";
                var result = await db
                    .ScriptEvaluateAsync(
                        SlidingWindowScript,
                        [new RedisKey(key)],
                        [nowMs, windowMs, permitLimit, member])
                    .ConfigureAwait(false);

                var tuple = (RedisResult[]?)result;
                if (tuple is not null && tuple.Length >= 2)
                {
                    var allowed = (long)tuple[0] == 1;
                    var retryAfter = (int)(long)tuple[1];
                    return (allowed, Math.Max(0, retryAfter), clientIp);
                }

                return (true, 0, clientIp);
            }

            var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds)).ConfigureAwait(false);
            }

            if (count > permitLimit)
            {
                return (false, windowSeconds, clientIp);
            }

            return (true, 0, clientIp);
        }

        if (_options.StrictDistributedMode)
        {
            _logger.LogWarning("Rate limit request denied because strict distributed mode is enabled and Redis is unavailable.");
            return (false, windowSeconds, clientIp);
        }

        // Compatibility fallback for environments where Redis is not configured.
        var fallbackCount = _memoryCache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(windowSeconds);
            return 0L;
        });

        fallbackCount++;
        _memoryCache.Set(key, fallbackCount, TimeSpan.FromSeconds(windowSeconds));

        if (fallbackCount > permitLimit)
        {
            _logger.LogWarning("Request throttled for {ClientIp} using in-memory fallback limiter.", clientIp);
            return (false, windowSeconds, clientIp);
        }

        return (true, 0, clientIp);
    }
}
