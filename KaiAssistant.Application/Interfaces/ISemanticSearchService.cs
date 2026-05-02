namespace KaiAssistant.Application.Interfaces;
public class EmbeddedDocument
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}
// IEmbeddingService is defined in a dedicated file under Interfaces to avoid duplicates
public class SemanticSearchResult
{
    public EmbeddedDocument Document { get; set; } = new();
    public float Similarity { get; set; }
    public int Rank { get; set; }
}
public interface ISemanticSearchService
{
    Task IndexDocumentAsync(EmbeddedDocument document, CancellationToken cancellationToken = default);
    Task IndexDocumentsAsync(IEnumerable<EmbeddedDocument> documents, CancellationToken cancellationToken = default);
    Task<IEnumerable<SemanticSearchResult>> SearchAsync(
        string query,
        int topK = 5,
        float minSimilarity = 0.5f,
        string? source = null,
        CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default);
}