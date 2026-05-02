using System.ComponentModel.DataAnnotations;
namespace KaiAssistant.Application.Options;
public sealed class SemanticCacheOptions
{
    public const string SectionName = "SemanticCache";
    public bool Enabled { get; set; } = true;
    [Range(0.5, 1.0)]
    public double SimilarityThreshold { get; set; } = 0.9;
    [Range(8, 4096)]
    public int EmbeddingDimensions { get; set; } = 64;
    [Range(60, 86400)]
    public int CacheTtlSeconds { get; set; } = 3600;
    [Range(1, 2000)]
    public int CandidateScanLimit { get; set; } = 300;
}