namespace KaiAssistant.Infrastructure.Services;
using global::KaiAssistant.Application.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    private const int DimensionSize = 384;
    private readonly ILogger<DeterministicEmbeddingService> _logger;
    public DeterministicEmbeddingService(ILogger<DeterministicEmbeddingService> logger)
    {
        _logger = logger;
    }
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = GenerateEmbedding(text);
        return Task.FromResult(embedding);
    }
    private float[] GenerateEmbedding(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new float[DimensionSize];
        }
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var seed = BitConverter.ToInt32(hash, 0);
        var random = new Random(seed);
        var embedding = new float[DimensionSize];
        float sum = 0f;
        for (int i = 0; i < DimensionSize; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5);
            sum += embedding[i] * embedding[i];
        }
        var norm = (float)Math.Sqrt(sum);
        if (norm > 0)
        {
            for (int i = 0; i < DimensionSize; i++)
            {
                embedding[i] /= norm;
            }
        }
        return embedding;
    }
}