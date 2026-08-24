using System.ComponentModel.DataAnnotations;

namespace KaiAssistant.Application.Options;

public sealed class RagIngestionOptions
{
    public const string SectionName = "RagIngestion";
    [Range(100, 4000)] public int MaxChunkChars { get; set; } = 1000;
    [Range(0, 1000)] public int ChunkOverlapChars { get; set; } = 150;
    [Range(1, 100)] public int RetrievalCandidateCount { get; set; } = 20;
    [Range(1, 20)] public int RerankedChunkCount { get; set; } = 6;
    [Range(1024, 52428800)] public long MaxFileSizeBytes { get; set; } = 5_242_880;
    [Range(1, 16)] public int MaxConcurrency { get; set; } = 2;
    [Range(1, 60)] public int PollingIntervalSeconds { get; set; } = 3;
    [Range(10, 3600)] public int LeaseDurationSeconds { get; set; } = 120;
    [Range(1, 20)] public int MaxAttempts { get; set; } = 3;
    [Range(1, 3600)] public int RetryBaseDelaySeconds { get; set; } = 5;
    public string[] AllowedMimeTypes { get; set; } = ["text/plain", "text/markdown", "application/json"];
}
