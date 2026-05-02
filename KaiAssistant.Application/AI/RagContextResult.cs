namespace KaiAssistant.Application.AI;
public sealed class RagContextResult
{
    public string AugmentedPrompt { get; init; } = string.Empty;
    public IReadOnlyList<RagSnippet> Snippets { get; init; } = Array.Empty<RagSnippet>();
}
public sealed class RagSnippet
{
    public string DocumentId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public double Similarity { get; init; }
}