using System.Security.Cryptography;
using System.Text;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Application.Rag;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KaiAssistant.Infrastructure.HostedServices;

/// <summary>Loads the small, repository-owned portfolio corpus once at startup. It is not a queue or worker.</summary>
public sealed class KnowledgeBootstrapHostedService(
    IMongoDatabase database,
    IHostEnvironment environment,
    IEmbeddingService embeddings,
    IOptions<KnowledgeOptions> options,
    IOptions<EmbeddingOptions> embeddingOptions,
    IOptions<RagOptions> ragOptions,
    IKnowledgeIndexStore indexes,
    ILogger<KnowledgeBootstrapHostedService> logger) : IHostedService
{
    private readonly IMongoCollection<KnowledgeDocument> _documents = database.GetCollection<KnowledgeDocument>("knowledge_base_documents");
    private readonly IMongoCollection<KnowledgeChunk> _chunks = database.GetCollection<KnowledgeChunk>("knowledge_chunks");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        var corpusPath = Path.IsPathRooted(config.CorpusPath)
            ? config.CorpusPath
            : Path.Combine(environment.ContentRootPath, config.CorpusPath);
        if (!Directory.Exists(corpusPath))
        {
            logger.LogWarning("Knowledge corpus path does not exist: {CorpusPath}", corpusPath);
            return;
        }

        var files = Directory.EnumerateFiles(corpusPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => new[] { ".md", ".txt", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in files)
        {
            await RefreshAsync(file, config, cancellationToken).ConfigureAwait(false);
        }
        var embedding = embeddingOptions.Value;
        await indexes.ActivateAsync(new KnowledgeIndexDescriptor(ragOptions.Value.IndexVersion, embedding.Provider, embedding.Model, embedding.Version, embedding.Dimensions), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Knowledge bootstrap completed. Files={FileCount}", files.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RefreshAsync(string file, KnowledgeOptions config, CancellationToken cancellationToken)
    {
        var source = Path.GetFileName(file);
        var content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var current = await _documents.Find(x => x.Source == source && (x.Status == "Completed" || x.Status == "Indexed"))
            .SortByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (current?.ContentHash == contentHash)
        {
            return;
        }

        var document = new KnowledgeDocument
        {
            Id = ObjectId.GenerateNewId().ToString(), Source = source, Title = Path.GetFileNameWithoutExtension(file),
            Content = content, ContentHash = contentHash, MimeType = MimeType(file), Status = "Processing",
            Metadata = new(StringComparer.OrdinalIgnoreCase) { ["source"] = source, ["title"] = Path.GetFileNameWithoutExtension(file), ["contentHash"] = contentHash }
        };
        var pieces = Split(content, config.ChunkSize, config.ChunkOverlap);
        await _documents.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            var embeddingConfig = embeddingOptions.Value;
            IReadOnlyList<float[]>? vectors = null;
            if (config.SemanticIndexingEnabled)
            {
                try
                {
                    vectors = await embeddings.GenerateEmbeddingsAsync(pieces, EmbeddingPurpose.RetrievalDocument,
                        Enumerable.Repeat<string?>(document.Title, pieces.Count).ToArray(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Semantic indexing failed for {Source}; retaining keyword-only knowledge.", source);
                }
            }
            var chunks = pieces.Select((piece, index) => new KnowledgeChunk
            {
                Id = ObjectId.GenerateNewId().ToString(), DocumentId = document.Id!, Source = source, Title = document.Title,
                Content = piece, ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(piece))).ToLowerInvariant(), ChunkIndex = index,
                Embedding = vectors is not null && index < vectors.Count ? vectors[index] : [],
                EmbeddingProvider = embeddingConfig.Provider, EmbeddingModel = embeddingConfig.Model,
                EmbeddingDimensions = vectors is not null ? embeddingConfig.Dimensions : 0,
                EmbeddingPipelineVersion = embeddingConfig.Version, IndexVersion = ragOptions.Value.IndexVersion,
                Metadata = new(StringComparer.OrdinalIgnoreCase) { ["source"] = source, ["title"] = document.Title, ["contentHash"] = contentHash, ["chunkIndex"] = index.ToString() }
            }).ToArray();
            if (chunks.Length > 0) await _chunks.InsertManyAsync(chunks, cancellationToken: cancellationToken).ConfigureAwait(false);
            document.Status = "Completed"; document.ChunkCount = chunks.Length; document.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _documents.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (current?.Id is not null)
                await _documents.UpdateOneAsync(x => x.Id == current.Id, Builders<KnowledgeDocument>.Update.Set(x => x.Status, "Retired"), cancellationToken: cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Knowledge source refreshed: {Source}, chunks={ChunkCount}, semantic={Semantic}", source, chunks.Length, vectors is not null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await _documents.UpdateOneAsync(x => x.Id == document.Id, Builders<KnowledgeDocument>.Update.Set(x => x.Status, "Failed"), cancellationToken: CancellationToken.None).ConfigureAwait(false);
            logger.LogError(ex, "Knowledge refresh failed for {Source}; existing knowledge remains active.", source);
        }
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".md" => "text/markdown", ".json" => "application/json", _ => "text/plain" };
    private static List<string> Split(string content, int size, int overlap)
    {
        var result = new List<string>();
        for (var start = 0; start < content.Length;)
        {
            var end = Math.Min(content.Length, start + size);
            if (end < content.Length) { var boundary = content.LastIndexOfAny(['.', '!', '?', '\n'], end - 1, end - start); if (boundary > start + size / 2) end = boundary + 1; }
            var piece = content[start..end].Trim(); if (piece.Length > 0) result.Add(piece);
            if (end == content.Length) break; start = Math.Max(start + 1, end - overlap);
        }
        return result;
    }
}
