using System.ComponentModel.DataAnnotations;
namespace KaiAssistant.Application.Options;
public sealed class RagOptions
{
    public const string SectionName = "Rag";
    public bool Enabled { get; set; } = true;
    [Range(1, 20)]
    public int TopK { get; set; } = 5;
    [Range(0.1, 1.0)]
    public double MinSimilarity { get; set; } = 0.7;
    [Range(200, 12000)]
    public int MaxContextChars { get; set; } = 4000;
}