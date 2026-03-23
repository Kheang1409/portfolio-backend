using System.ComponentModel.DataAnnotations;

namespace KaiAssistant.API.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 120;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    public bool UseSlidingWindow { get; set; } = true;

    [Range(1, 10)]
    public int BurstMultiplier { get; set; } = 1;

    [Range(1, 100_000)]
    public int? GlobalPermitLimit { get; set; }

    public bool StrictDistributedMode { get; set; } = false;

    public string[] PathPrefixes { get; set; } = ["/api"];
}
