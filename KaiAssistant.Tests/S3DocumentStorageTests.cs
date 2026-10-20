using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using KaiAssistant.Application.Options;
using KaiAssistant.Infrastructure.AI.Rag;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KaiAssistant.Tests;

public sealed class S3DocumentStorageTests
{
    [Fact]
    public async Task Store_uploads_to_configured_bucket_and_prefix()
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? captured = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());
        var storage = new S3DocumentStorage(s3.Object, Options.Create(new RagIngestionOptions
        {
            StorageProvider = "S3",
            S3Bucket = "knowledge-bucket",
            S3KeyPrefix = "uploads/portfolio"
        }));

        var key = await storage.StoreAsync(new MemoryStream([1, 2, 3]), "resume.md");

        key.Should().StartWith("uploads/portfolio/");
        key.Should().EndWith("-resume.md");
        captured.Should().NotBeNull();
        captured!.BucketName.Should().Be("knowledge-bucket");
        captured.Key.Should().Be(key);
        captured.AutoCloseStream.Should().BeFalse();
    }

    [Fact]
    public async Task OpenRead_returns_document_content_and_rejects_unsafe_keys()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync("knowledge-bucket", "uploads/document.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                BucketName = "knowledge-bucket",
                Key = "uploads/document.txt",
                ContentLength = 5,
                ResponseStream = new MemoryStream([104, 101, 108, 108, 111])
            });
        var storage = new S3DocumentStorage(s3.Object, Options.Create(new RagIngestionOptions
        {
            StorageProvider = "S3",
            S3Bucket = "knowledge-bucket",
            S3KeyPrefix = "uploads"
        }));

        await using var content = await storage.OpenReadAsync("uploads/document.txt");
        using var reader = new StreamReader(content);
        (await reader.ReadToEndAsync()).Should().Be("hello");

        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync("../outside.txt"));
        s3.Verify(x => x.GetObjectAsync("knowledge-bucket", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
