using System.Text.RegularExpressions;
using KaiAssistant.Application.Interfaces;
namespace KaiAssistant.Infrastructure.AI;
public sealed class PromptSecurityService : IPromptSecurityService
{
    private static readonly Regex ControlCharsRegex = new("[\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F]", RegexOptions.Compiled);
    private static readonly Regex ScriptTagRegex = new("<\\s*script[^>]*>.*?<\\s*/\\s*script\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly string[] InjectionIndicators =
    [
        "ignore previous instructions",
        "disregard all prior",
        "reveal system prompt",
        "bypass safety",
        "developer mode"
    ];
    public string SanitizeInput(string input)
    {
        var sanitized = ControlCharsRegex.Replace(input ?? string.Empty, string.Empty).Trim();
        return sanitized.Length > 4000 ? sanitized[..4000] : sanitized;
    }
    public string SanitizeOutput(string output)
    {
        var sanitized = ScriptTagRegex.Replace(output ?? string.Empty, string.Empty).Trim();
        return sanitized;
    }
    public bool LooksLikePromptInjection(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        return InjectionIndicators.Any(x => input.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}