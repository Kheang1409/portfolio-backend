using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
namespace KaiAssistant.Infrastructure.AI.Caching;
public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    private readonly IOptionsMonitor<SemanticCacheOptions> _options;
    public DeterministicEmbeddingService(IOptionsMonitor<SemanticCacheOptions> options)
    {
        _options = options;
    }
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        var dimensions = _options.CurrentValue.EmbeddingDimensions;
        var vector = new float[dimensions];
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult(vector);
        }
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        for (var i = 0; i < dimensions; i++)
        {
            var source = hash[i % hash.Length];
            vector[i] = (source / 255f) * 2f - 1f;
        }
        NormalizeInPlace(vector);
        return Task.FromResult(vector);
    }
    private static void NormalizeInPlace(float[] vector)
    {
        double sumSquares = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }
        if (sumSquares <= 0)
        {
            return;
        }
        var length = (float)Math.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= length;
        }
    }
}