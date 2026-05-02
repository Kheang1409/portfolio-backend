using KaiAssistant.Application.AI;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
namespace KaiAssistant.Infrastructure.AI.Rag;
public sealed class RagService : IRagService
{
    private static readonly ProjectionDefinition<KnowledgeDocument, RagDocumentProjection> RagProjection =
        Builders<KnowledgeDocument>.Projection.Expression(x => new RagDocumentProjection
        {
            Id = x.Id,
            Source = x.Source,
            Content = x.Content,
            Embedding = x.Embedding
        });
    private readonly IMongoCollection<KnowledgeDocument> _collection;
    private readonly IEmbeddingService _embeddingService;
    private readonly IOptionsMonitor<RagOptions> _options;
    private readonly IFeatureFlagService _featureFlags;
    private readonly ILogger<RagService> _logger;
    public RagService(
        IMongoDatabase database,
        IEmbeddingService embeddingService,
        IOptionsMonitor<RagOptions> options,
        IFeatureFlagService featureFlags,
        ILogger<RagService> logger)
    {
        _collection = database.GetCollection<KnowledgeDocument>("knowledge_base_documents");
        _embeddingService = embeddingService;
        _options = options;
        _featureFlags = featureFlags;
        _logger = logger;
    }
    public async Task<RagContextResult> BuildAugmentedPromptAsync(string question, CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled || !_featureFlags.EnableRag)
        {
            return new RagContextResult { AugmentedPrompt = question };
        }
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken).ConfigureAwait(false);
        var documents = await _collection
            .Find(Builders<KnowledgeDocument>.Filter.Empty)
            .Project(RagProjection)
            .Limit(300)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var scored = documents
            .Select(x => new
            {
                Document = x,
                Similarity = VectorMath.CosineSimilarity(queryEmbedding, x.Embedding)
            })
            .Where(x => x.Similarity >= _options.CurrentValue.MinSimilarity)
            .OrderByDescending(x => x.Similarity)
            .Take(Math.Max(1, _options.CurrentValue.TopK))
            .ToList();
        if (scored.Count == 0)
        {
            return new RagContextResult { AugmentedPrompt = question };
        }
        _logger.LogInformation("RAG context retrieved. RetrievedSnippets={Count}", scored.Count);
        var snippets = scored
            .Select(x => new RagSnippet
            {
                DocumentId = x.Document.Id ?? string.Empty,
                Source = x.Document.Source,
                Content = x.Document.Content,
                Similarity = x.Similarity
            })
            .ToList();
        var contextLines = new List<string>(snippets.Count);
        for (var i = 0; i < snippets.Count; i++)
        {
            var x = snippets[i];
            contextLines.Add($"[{i + 1}] source={x.Source}; relevance={x.Similarity:F3}; content={x.Content}");
        }
        var contextBlock = string.Join('\n', contextLines);
        if (contextBlock.Length > _options.CurrentValue.MaxContextChars)
        {
            contextBlock = contextBlock[.._options.CurrentValue.MaxContextChars];
        }
        var augmented =
            "Use the following knowledge base context when relevant. If context is insufficient, say what is missing.\n\n" +
            contextBlock +
            "\n\nUser question:\n" +
            question;
        return new RagContextResult
        {
            AugmentedPrompt = augmented,
            Snippets = snippets
        };
    }
    private sealed class RagDocumentProjection
    {
        public string? Id { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public float[] Embedding { get; init; } = Array.Empty<float>();
    }
}