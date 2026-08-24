using System.Diagnostics.Metrics;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace KaiAssistant.Infrastructure.AI.Rag;

internal sealed class KnowledgeIndexState
{
    [BsonId] public string Id { get; set; } = "active";
    public KnowledgeIndexDescriptor Active { get; set; } = new("knowledge-v1", "deterministic", "sha256-projection", "embedding-v1", 64);
    public KnowledgeIndexDescriptor? Previous { get; set; }
}

public sealed class MongoKnowledgeIndexStore(IMongoDatabase database, IOptionsMonitor<RagOptions> ragOptions) : IKnowledgeIndexStore
{
    private readonly IMongoCollection<KnowledgeIndexState> _states = database.GetCollection<KnowledgeIndexState>("knowledge_index_state");
    public async Task<KnowledgeIndexDescriptor> GetActiveAsync(CancellationToken cancellationToken = default) =>
        (await _states.Find(x => x.Id == "active").FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false))?.Active
        ?? new(ragOptions.CurrentValue.IndexVersion, "deterministic", "sha256-projection", "embedding-v1", 64);
    public async Task ActivateAsync(KnowledgeIndexDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var current = await _states.Find(x => x.Id == "active").FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var state = new KnowledgeIndexState { Active = descriptor, Previous = current?.Active ?? await GetActiveAsync(cancellationToken).ConfigureAwait(false) };
        await _states.ReplaceOneAsync(x => x.Id == "active", state, new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
    }
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var current = await _states.Find(x => x.Id == "active").FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (current?.Previous is null) throw new InvalidOperationException("No previous knowledge index is available for rollback.");
        (current.Active, current.Previous) = (current.Previous, current.Active);
        await _states.ReplaceOneAsync(x => x.Id == "active", current, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

public sealed class KnowledgeReindexService(IMongoDatabase database, IDocumentIngestionService ingestion,
    IKnowledgeIndexStore indexes, IOptionsMonitor<EmbeddingOptions> options) : IKnowledgeReindexService
{
    private static readonly Meter Meter = new("KaiAssistant.Embeddings", "1.0.0");
    private static readonly Counter<long> ReindexedChunks = Meter.CreateCounter<long>("embedding_reindex_chunks_total");
    private readonly IMongoCollection<KnowledgeDocument> _documents = database.GetCollection<KnowledgeDocument>("knowledge_base_documents");
    private readonly IMongoCollection<KnowledgeChunk> _chunks = database.GetCollection<KnowledgeChunk>("knowledge_chunks");
    public async Task<ReindexResult> StageAllAsync(CancellationToken cancellationToken = default)
    {
        var target = options.CurrentValue;
        var documents = await _documents.Find(x => x.Status == "Completed" || x.Status == "Indexed").ToListAsync(cancellationToken).ConfigureAwait(false);
        var completed = 0; var chunks = 0;
        foreach (var document in documents)
        {
            var count = await ingestion.IndexExistingAsync(document.Id!, document.Source, document.Title, document.MimeType, document.Content, document.Metadata, cancellationToken).ConfigureAwait(false);
            var persisted = await _chunks.CountDocumentsAsync(x => x.DocumentId == document.Id && x.IndexVersion == target.IndexVersion && x.EmbeddingDimensions == target.Dimensions, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (persisted != count) return new(target.IndexVersion, documents.Count, completed, chunks, false);
            completed++; chunks += count; ReindexedChunks.Add(count);
        }
        return new(target.IndexVersion, documents.Count, completed, chunks, documents.Count > 0 && completed == documents.Count);
    }
    public async Task ActivateStagedAsync(CancellationToken cancellationToken = default)
    {
        var target = options.CurrentValue;
        var documents = await _documents.Find(x => x.Status == "Completed" || x.Status == "Indexed").ToListAsync(cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0) throw new InvalidOperationException("The staged knowledge index has no active documents and cannot be activated.");
        var staged = await _chunks.Find(x => x.IndexVersion == target.IndexVersion).ToListAsync(cancellationToken).ConfigureAwait(false);
        var active = await indexes.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KnowledgeChunk> activeChunks = active is null || active.IndexVersion == target.IndexVersion
            ? Array.Empty<KnowledgeChunk>()
            : await _chunks.Find(x => x.IndexVersion == active.IndexVersion).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            if (document.Id is null) throw new InvalidOperationException("An active document has no identifier.");
            var documentStaged = staged.Where(x => x.DocumentId == document.Id).ToArray();
            var expectedOrdinals = activeChunks.Where(x => x.DocumentId == document.Id).Select(x => x.ChunkIndex).Distinct().Order().ToArray();
            if (expectedOrdinals.Length == 0 && document.ChunkCount > 0) expectedOrdinals = Enumerable.Range(0, document.ChunkCount).ToArray();
            var stagedOrdinals = documentStaged.Select(x => x.ChunkIndex).Order().ToArray();
            var compatible = documentStaged.All(x =>
                x.EmbeddingProvider == target.Provider && x.EmbeddingModel == target.Model &&
                x.EmbeddingPipelineVersion == target.Version && x.EmbeddingDimensions == target.Dimensions &&
                x.Embedding.Length == target.Dimensions && x.Embedding.All(float.IsFinite));
            var uniqueLogicalIdentities = documentStaged.Select(x => (x.DocumentId, x.ChunkIndex, x.EmbeddingPipelineVersion, x.IndexVersion)).Distinct().Count() == documentStaged.Length;
            if (expectedOrdinals.Length == 0 || !expectedOrdinals.SequenceEqual(stagedOrdinals) || !compatible || !uniqueLogicalIdentities)
                throw new InvalidOperationException("The staged knowledge index is incomplete or incompatible and cannot be activated.");
        }
        await indexes.ActivateAsync(new(target.IndexVersion, target.Provider, target.Model, target.Version, target.Dimensions), cancellationToken).ConfigureAwait(false);
    }
    public Task RollbackAsync(CancellationToken cancellationToken = default) => indexes.RollbackAsync(cancellationToken);
}
