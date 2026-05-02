namespace KaiAssistant.Infrastructure.Services;
using global::KaiAssistant.Application.Interfaces;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
public sealed class InMemorySemanticSearchService : ISemanticSearchService
{
    private readonly ConcurrentDictionary<string, EmbeddedDocument> _documents = new();
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<InMemorySemanticSearchService> _logger;
    public InMemorySemanticSearchService(
        IEmbeddingService embeddingService,
        ILogger<InMemorySemanticSearchService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }
    public async Task IndexDocumentAsync(EmbeddedDocument document, CancellationToken cancellationToken = default)
    {
        if (document.Embedding.Length == 0)
        {
            document.Embedding = await _embeddingService.GenerateEmbeddingAsync(document.Content, cancellationToken)
                .ConfigureAwait(false);
        }
        _documents.AddOrUpdate(document.Id, document, (_, _) => document);
        _logger.LogInformation("Indexed document: Id={Id}, Source={Source}", document.Id, document.Source);
    }
    public async Task IndexDocumentsAsync(IEnumerable<EmbeddedDocument> documents, CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        foreach (var doc in docList)
        {
            if (doc.Embedding.Length == 0)
            {
                doc.Embedding = await _embeddingService.GenerateEmbeddingAsync(doc.Content, cancellationToken)
                    .ConfigureAwait(false);
            }
            _documents.AddOrUpdate(doc.Id, doc, (_, _) => doc);
        }
        _logger.LogInformation("Indexed {Count} documents", docList.Count);
    }
    public async Task<IEnumerable<SemanticSearchResult>> SearchAsync(
        string query,
        int topK = 5,
        float minSimilarity = 0.5f,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<SemanticSearchResult>();
        }
        // Generate embedding for query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken)
            .ConfigureAwait(false);
        // Calculate similarity for all documents
        var results = _documents.Values
            .AsParallel()
            .Where(d => source == null || d.Source == source)
            .Select((doc, idx) => new
            {
                Document = doc,
                Similarity = CosineSimilarity(queryEmbedding, doc.Embedding)
            })
            .Where(r => r.Similarity >= minSimilarity)
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .Select((item, rank) => new SemanticSearchResult
            {
                Document = item.Document,
                Similarity = item.Similarity,
                Rank = rank + 1
            })
            .ToList();
        _logger.LogInformation(
            "Semantic search: query={Query}, results={Count}, source={Source}",
            query[..Math.Min(50, query.Length)],
            results.Count,
            source ?? "all");
        return results;
    }
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _documents.Clear();
        _logger.LogInformation("Cleared all indexed documents");
        return Task.CompletedTask;
    }
    public Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.Count);
    }
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Embeddings must have same dimension");
        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denominator = (float)Math.Sqrt(normA) * (float)Math.Sqrt(normB);
        return denominator == 0 ? 0f : dotProduct / denominator;
    }
}