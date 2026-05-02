using System.Diagnostics;
using KaiAssistant.Application.AI;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
namespace KaiAssistant.Infrastructure.AI.Caching;
public sealed class SemanticCacheService : ISemanticCacheService
{
    private static readonly ActivitySource ActivitySource = new("KaiAssistant.SemanticCache", "1.0.0");
    private static readonly ProjectionDefinition<CachedPrompt, CacheCandidate> CandidateProjection =
        Builders<CachedPrompt>.Projection.Expression(x => new CacheCandidate
        {
            Embedding = x.Embedding,
            Response = x.Response
        });
    private readonly IMongoCollection<CachedPrompt> _collection;
    private readonly IEmbeddingService _embeddingService;
    private readonly IOptionsMonitor<SemanticCacheOptions> _options;
    private readonly IFeatureFlagService _featureFlags;
    private readonly ICacheService? _cacheService;
    private readonly ILogger<SemanticCacheService> _logger;
    public SemanticCacheService(
        IMongoDatabase database,
        IEmbeddingService embeddingService,
        IOptionsMonitor<SemanticCacheOptions> options,
        IFeatureFlagService featureFlags,
        ILogger<SemanticCacheService> logger,
        ICacheService? cacheService = null)
    {
        _collection = database.GetCollection<CachedPrompt>("cached_prompts");
        _embeddingService = embeddingService;
        _options = options;
        _featureFlags = featureFlags;
        _logger = logger;
        _cacheService = cacheService;
    }
    public async Task<AiResponse?> TryGetAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || !_featureFlags.EnableSemanticCaching)
        {
            return null;
        }
        using var activity = ActivitySource.StartActivity("ai.cache_hit", ActivityKind.Internal);
        if (_cacheService is not null)
        {
            var hotKey = $"semantic-hot:{ComputeKey(prompt)}";
            var hot = await _cacheService.GetAsync<string>(hotKey, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(hot))
            {
                activity?.SetTag("ai.cache.layer", "hot");
                activity?.SetTag("ai.cache.hit", true);
                return new AiResponse
                {
                    Content = hot,
                    ModelUsed = "semantic-cache-hot",
                    LatencyMs = 1,
                    FallbackUsed = false
                };
            }
        }
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(prompt, cancellationToken).ConfigureAwait(false);
        var candidates = await _collection
            .Find(Builders<CachedPrompt>.Filter.Empty)
            .SortByDescending(x => x.CreatedAt)
            .Project(CandidateProjection)
            .Limit(Math.Max(1, options.CandidateScanLimit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        CacheCandidate? best = null;
        var bestScore = double.MinValue;
        foreach (var candidate in candidates)
        {
            if (candidate.Embedding.Length == 0)
            {
                continue;
            }
            var similarity = VectorMath.CosineSimilarity(queryEmbedding, candidate.Embedding);
            if (similarity > bestScore)
            {
                bestScore = similarity;
                best = candidate;
            }
        }
        if (best is null || bestScore < options.SimilarityThreshold)
        {
            activity?.SetTag("ai.cache.hit", false);
            return null;
        }
        _logger.LogInformation("Cache hit. Semantic cache similarity={Similarity:F4}", bestScore);
        activity?.SetTag("ai.cache.hit", true);
        activity?.SetTag("ai.cache.similarity", bestScore);
        return new AiResponse
        {
            Content = best.Response,
            ModelUsed = "semantic-cache",
            LatencyMs = 1,
            FallbackUsed = false
        };
    }
    public async Task SetAsync(string prompt, AiResponse response, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || !_featureFlags.EnableSemanticCaching)
        {
            return;
        }
        var embedding = await _embeddingService.GenerateEmbeddingAsync(prompt, cancellationToken).ConfigureAwait(false);
        var document = new CachedPrompt
        {
            Prompt = prompt,
            Embedding = embedding,
            Response = response.Content,
            CreatedAt = DateTime.UtcNow
        };
        await _collection.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (_cacheService is not null)
        {
            var hotKey = $"semantic-hot:{ComputeKey(prompt)}";
            await _cacheService
                .SetAsync(hotKey, response.Content, TimeSpan.FromSeconds(options.CacheTtlSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
    }
    private static string ComputeKey(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }
    private sealed class CacheCandidate
    {
        public float[] Embedding { get; init; } = Array.Empty<float>();
        public string Response { get; init; } = string.Empty;
    }
}