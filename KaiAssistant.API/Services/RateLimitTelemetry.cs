using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaiAssistant.API.Services;

public interface IRateLimitTelemetry
{
    void RecordAllowed(string ip, string path);
    void RecordBlocked(string ip, string path);
    RateLimitTelemetrySnapshot Snapshot(int minutes, int top);
}

public sealed class RateLimitTelemetrySnapshot
{
    public long TotalBlockedRequests { get; set; }
    public long TotalAllowedRequests { get; set; }
    public IReadOnlyList<RateLimitIpCount> TopIps { get; set; } = Array.Empty<RateLimitIpCount>();
}

public sealed class RateLimitIpCount
{
    public string Ip { get; set; } = string.Empty;
    public long Count { get; set; }
}

public sealed class RateLimitTelemetry : IRateLimitTelemetry
{
    private const string TotalBlockedKey = "ratelimit:telemetry:total:blocked";
    private const string TotalAllowedKey = "ratelimit:telemetry:total:allowed";

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RateLimitTelemetry> _logger;
    private readonly ConcurrentDictionary<string, long> _fallbackBlockedWindowCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _fallbackAllowedWindowCounts = new(StringComparer.OrdinalIgnoreCase);
    private long _totalBlocked;
    private long _totalAllowed;

    public RateLimitTelemetry(IServiceProvider serviceProvider, ILogger<RateLimitTelemetry> logger)
    {
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _logger = logger;
    }

    public void RecordAllowed(string ip, string path)
    {
        var minuteWindow = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");

        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            db.StringIncrement(TotalAllowedKey);
            var windowKey = $"ratelimit:telemetry:allowed:{minuteWindow}";
            db.StringIncrement(windowKey);
            db.KeyExpire(windowKey, TimeSpan.FromMinutes(90));
            return;
        }

        Interlocked.Increment(ref _totalAllowed);
        var key = $"allowed:{minuteWindow}";
        _fallbackAllowedWindowCounts.AddOrUpdate(key, 1, (_, old) => old + 1);
    }

    public void RecordBlocked(string ip, string path)
    {
        Interlocked.Increment(ref _totalBlocked);

        var minuteWindow = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");

        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            db.StringIncrement(TotalBlockedKey);
            var hashKey = $"ratelimit:telemetry:blocked:{minuteWindow}";
            var safeIp = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
            var current = db.HashIncrement(hashKey, safeIp, 1);
            db.KeyExpire(hashKey, TimeSpan.FromMinutes(90));

            if (current % 10 == 0)
            {
                _logger.LogWarning(
                    "RateLimitAggregate: path={Path} ip={Ip} blockedCount={BlockedCount} window={Window}",
                    path,
                    ip,
                    current,
                    minuteWindow);
            }

            return;
        }

        var key = $"blocked:{minuteWindow}:{ip}";
        var currentMem = _fallbackBlockedWindowCounts.AddOrUpdate(key, 1, (_, old) => old + 1);

        if (currentMem % 10 == 0)
        {
            _logger.LogWarning(
                "RateLimitAggregate: path={Path} ip={Ip} blockedCount={BlockedCount} window={Window}",
                path,
                ip,
                currentMem,
                minuteWindow);
        }
    }

    public RateLimitTelemetrySnapshot Snapshot(int minutes, int top)
    {
        var boundedMinutes = Math.Clamp(minutes, 1, 60);
        var boundedTop = Math.Clamp(top, 1, 20);

        var grouped = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long blockedTotal;
        long allowedTotal;

        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            blockedTotal = (long?)db.StringGet(TotalBlockedKey) ?? 0;
            allowedTotal = (long?)db.StringGet(TotalAllowedKey) ?? 0;

            for (var i = 0; i < boundedMinutes; i++)
            {
                var window = DateTimeOffset.UtcNow.AddMinutes(-i).ToString("yyyyMMddHHmm");
                var hashKey = $"ratelimit:telemetry:blocked:{window}";
                foreach (var entry in db.HashGetAll(hashKey))
                {
                    var ip = entry.Name.ToString();
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        continue;
                    }

                    var value = (long)entry.Value;
                    grouped[ip] = grouped.TryGetValue(ip, out var count) ? count + value : value;
                }
            }
        }
        else
        {
            blockedTotal = Interlocked.Read(ref _totalBlocked);
            allowedTotal = Interlocked.Read(ref _totalAllowed);
            var earliest = DateTimeOffset.UtcNow.AddMinutes(-boundedMinutes).ToString("yyyyMMddHHmm");

            foreach (var item in _fallbackBlockedWindowCounts)
            {
                if (!item.Key.StartsWith("blocked:", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = item.Key.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }

                var window = parts[1];
                if (string.Compare(window, earliest, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                var ip = parts[2];
                grouped[ip] = grouped.TryGetValue(ip, out var existing) ? existing + item.Value : item.Value;
            }
        }

        var topIps = grouped
            .OrderByDescending(x => x.Value)
            .Take(boundedTop)
            .Select(x => new RateLimitIpCount
            {
                Ip = AnonymizeIp(x.Key),
                Count = x.Value
            })
            .ToList();

        return new RateLimitTelemetrySnapshot
        {
            TotalBlockedRequests = blockedTotal,
            TotalAllowedRequests = allowedTotal,
            TopIps = topIps
        };
    }

    private static string AnonymizeIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return "unknown";
        }

        if (ip.Contains(':', StringComparison.Ordinal))
        {
            var parts = ip.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2)
            {
                return "ipv6:masked";
            }

            return string.Join(':', parts.Take(2)) + ":****";
        }

        var octets = ip.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (octets.Length != 4)
        {
            return "ip:masked";
        }

        return $"{octets[0]}.{octets[1]}.***.***";
    }
}
