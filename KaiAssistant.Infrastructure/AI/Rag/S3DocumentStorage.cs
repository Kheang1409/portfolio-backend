using Amazon.S3;
using Amazon.S3.Model;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using Microsoft.Extensions.Options;

namespace KaiAssistant.Infrastructure.AI.Rag;

public sealed class S3DocumentStorage : IDocumentStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly long _maxFileSizeBytes;

    public S3DocumentStorage(IAmazonS3 s3, IOptions<RagIngestionOptions> options)
    {
        _s3 = s3;
        var config = options.Value;
        _bucket = string.IsNullOrWhiteSpace(config.S3Bucket)
            ? throw new InvalidOperationException("RagIngestion:S3Bucket is required when S3 storage is selected.")
            : config.S3Bucket;
        _prefix = config.S3KeyPrefix.Trim('/');
        _maxFileSizeBytes = config.MaxFileSizeBytes;
    }

    public async Task<string> StoreAsync(Stream content, string safeFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFileName);
        var keyPrefix = string.IsNullOrEmpty(_prefix) ? string.Empty : $"{_prefix}/";
        var key = $"{keyPrefix}{Guid.NewGuid():N}-{Path.GetFileName(safeFileName)}";
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false
        }, cancellationToken).ConfigureAwait(false);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var expectedPrefix = string.IsNullOrEmpty(_prefix) ? string.Empty : $"{_prefix}/";
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains('\\') ||
            !key.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            key.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The S3 object key is invalid.", nameof(key));
        }

        using var response = await _s3.GetObjectAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
        if (response.ContentLength > _maxFileSizeBytes)
        {
            throw new InvalidDataException("The stored document exceeds the configured size limit.");
        }

        var content = new MemoryStream((int)Math.Max(0, response.ContentLength));
        var buffer = new byte[81920];
        long totalRead = 0;
        while (true)
        {
            var read = await response.ResponseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
            if (totalRead > _maxFileSizeBytes)
            {
                content.Dispose();
                throw new InvalidDataException("The stored document exceeds the configured size limit.");
            }
            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        content.Position = 0;
        return content;
    }
}
