using KaiAssistant.Application.Rag;

namespace KaiAssistant.Application.Interfaces;

public interface IVectorStore
{
    Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
    Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default);
}

public interface IKeywordSearch
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default);
}

public interface IHybridRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, CancellationToken cancellationToken = default);
    Task<RetrievalDiagnostics> DiagnoseAsync(string query, RetrievalStrategy strategy, CancellationToken cancellationToken = default);
}

public interface IRagContextSelector
{
    IReadOnlyList<RetrievedChunk> Select(IReadOnlyList<RetrievedChunk> candidates);
}


public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(DocumentIngestionRequest request, CancellationToken cancellationToken = default);
    Task<int> IndexExistingAsync(string documentId, string source, string title, string mimeType, string content, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
}

public sealed record KnowledgeIndexDescriptor(string IndexVersion, string EmbeddingProvider, string EmbeddingModel, string EmbeddingVersion, int Dimensions);
public sealed record ReindexResult(string IndexVersion, int DocumentsTotal, int DocumentsCompleted, int ChunksEmbedded, bool ReadyForActivation);
public interface IKnowledgeIndexStore
{
    Task<KnowledgeIndexDescriptor> GetActiveAsync(CancellationToken cancellationToken = default);
    Task ActivateAsync(KnowledgeIndexDescriptor descriptor, CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
public interface IKnowledgeReindexService
{
    Task<ReindexResult> StageAllAsync(CancellationToken cancellationToken = default);
    Task ActivateStagedAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
