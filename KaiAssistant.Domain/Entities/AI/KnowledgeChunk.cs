using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace KaiAssistant.Domain.Entities.AI;

public sealed class KnowledgeChunk
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string EmbeddingProvider { get; set; } = "deterministic";
    public string EmbeddingModel { get; set; } = "sha256-projection";
    public int EmbeddingDimensions { get; set; } = 64;
    public string EmbeddingPipelineVersion { get; set; } = "embedding-v1";
    public string IndexVersion { get; set; } = "knowledge-v1";
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
