using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace KaiAssistant.Domain.Entities.AI;

public enum IngestionJobStatus { Queued, Processing, Completed, Failed, DeadLetter, Cancelled }

public sealed class IngestionJob
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string? Id { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Queued;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WorkerId { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? CorrelationId { get; set; }
}
