using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Application.Rag;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KaiAssistant.Infrastructure.AI.Rag;

/// <summary>Idempotent text ingestion. HTTP upload/adapters can enqueue this service in a worker later.</summary>
public sealed class DocumentIngestionService(
    IMongoDatabase database,
    IEmbeddingService embeddings,
    IVectorStore vectors,
    IOptionsMonitor<RagIngestionOptions> options,
    IOptionsMonitor<EmbeddingOptions> embeddingOptions,
    ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
    private readonly IMongoCollection<KnowledgeDocument> _documents = database.GetCollection<KnowledgeDocument>("knowledge_base_documents");

    public async Task<DocumentIngestionResult> IngestAsync(DocumentIngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);
        var normalized = Normalize(request.Content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        var existing = await _documents.Find(x => x.ContentHash == hash).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing?.Id is not null) return new DocumentIngestionResult(existing.Id, true, 0);

        var document = new KnowledgeDocument
        {
            Id = ObjectId.GenerateNewId().ToString(), Source = request.Source, Title = request.Title,
            Content = normalized, ContentHash = hash, MimeType = request.MimeType, Status = "Processing",
            Metadata = request.Metadata is null ? new(StringComparer.OrdinalIgnoreCase) : new(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };
        await _documents.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            var pieces = await IndexExistingAsync(document.Id!, request.Source, request.Title, request.MimeType, normalized, document.Metadata, cancellationToken).ConfigureAwait(false);
            document.Status = "Completed"; document.ChunkCount = pieces; document.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _documents.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Knowledge document indexed: {DocumentId}, chunks={ChunkCount}", document.Id, pieces);
            return new DocumentIngestionResult(document.Id!, false, pieces);
        }
        catch
        {
            document.Status = "Failed"; document.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _documents.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
    public async Task<int> IndexExistingAsync(string documentId, string source, string title, string mimeType, string content, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        var pieces = Split(Normalize(content), options.CurrentValue.MaxChunkChars, options.CurrentValue.ChunkOverlapChars);
        var embeddingConfig = embeddingOptions.CurrentValue;
        var embeddingsForPieces = await embeddings.GenerateEmbeddingsAsync(pieces, EmbeddingPurpose.RetrievalDocument,
            Enumerable.Repeat<string?>(title, pieces.Count).ToArray(), cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < pieces.Count; i++)
        {
            Dictionary<string, string> itemMetadata = metadata is null ? new(StringComparer.OrdinalIgnoreCase) : new(metadata, StringComparer.OrdinalIgnoreCase);
            itemMetadata["source"] = source; itemMetadata["title"] = title; itemMetadata["chunkIndex"] = i.ToString(); itemMetadata["contentType"] = mimeType;
            itemMetadata["version"] = "1"; itemMetadata["embeddingProvider"] = embeddingConfig.Provider;
            itemMetadata["embeddingModel"] = embeddingConfig.Model; itemMetadata["embeddingPipelineVersion"] = embeddingConfig.Version;
            itemMetadata["indexVersion"] = embeddingConfig.IndexVersion; itemMetadata["embeddingDimensions"] = embeddingConfig.Dimensions.ToString();
            var identity = CreateChunkId(documentId, i, embeddingConfig.IndexVersion);
            await vectors.UpsertAsync(new VectorRecord(identity, documentId, pieces[i], embeddingsForPieces[i], itemMetadata), cancellationToken).ConfigureAwait(false);
        }
        return pieces.Count;
    }
    public static string CreateChunkId(string documentId, int chunkIndex, string indexVersion) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId}:{chunkIndex}:{indexVersion}"))).ToLowerInvariant()[..24];

    private static string Normalize(string input) => Regex.Replace(input.Trim(), "\\s+", " ");
    private static List<string> Split(string content, int maxChars, int overlap)
    {
        var chunks = new List<string>(); var start = 0;
        while (start < content.Length)
        {
            var end = Math.Min(content.Length, start + maxChars);
            if (end < content.Length)
            {
                var boundary = content.LastIndexOfAny(['.', '!', '?', ';', '\n'], end - 1, end - start);
                if (boundary > start + maxChars / 2) end = boundary + 1;
            }
            chunks.Add(content[start..end].Trim());
            if (end == content.Length) break;
            start = Math.Max(end - overlap, start + 1);
        }
        return chunks.Where(x => x.Length > 0).ToList();
    }
}
