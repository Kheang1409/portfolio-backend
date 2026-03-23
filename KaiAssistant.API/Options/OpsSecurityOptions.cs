using System.ComponentModel.DataAnnotations;

namespace KaiAssistant.API.Options;

public sealed class OpsSecurityOptions
{
    public const string SectionName = "OpsSecurity";

    public bool Enabled { get; set; } = false;

    [Required]
    public string HeaderName { get; set; } = "X-Ops-Key";

    public string ApiKey { get; set; } = string.Empty;
}
