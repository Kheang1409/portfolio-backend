using System;
using System.Threading.Tasks;
using FluentAssertions;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Infrastructure.AI.Rag;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace KaiAssistant.Tests.Integration;

public sealed class KnowledgeIndexSwitchTests
{
    [Fact]
    public async Task Activation_is_atomic_and_rollback_restores_previous_descriptor()
    {
        using var runner = MongoTestRunner.Start();
        var database = new MongoClient(runner.ConnectionString).GetDatabase("index-switch-" + Guid.NewGuid().ToString("N"));
        var rag = new Mock<IOptionsMonitor<RagOptions>>(); rag.SetupGet(x => x.CurrentValue).Returns(new RagOptions { IndexVersion = "knowledge-v1" });
        var store = new MongoKnowledgeIndexStore(database, rag.Object);
        (await store.GetActiveAsync()).IndexVersion.Should().Be("knowledge-v1");
        var next = new KnowledgeIndexDescriptor("knowledge-v2", "gemini", "gemini-embedding-001", "gemini-v1", 768);
        await store.ActivateAsync(next);
        (await store.GetActiveAsync()).Should().Be(next);
        await store.RollbackAsync();
        (await store.GetActiveAsync()).IndexVersion.Should().Be("knowledge-v1");
    }

    [Fact]
    public async Task Incomplete_staged_index_cannot_activate()
    {
        using var runner = MongoTestRunner.Start();
        var database = new MongoClient(runner.ConnectionString).GetDatabase("index-incomplete-" + Guid.NewGuid().ToString("N"));
        await database.GetCollection<KnowledgeDocument>("knowledge_base_documents").InsertOneAsync(new KnowledgeDocument
            { Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), Status = "Completed", ContentHash = "hash", Content = "content" });
        var indexStore = new Mock<IKnowledgeIndexStore>();
        var ingestion = new Mock<IDocumentIngestionService>();
        var embedding = new Mock<IOptionsMonitor<EmbeddingOptions>>(); embedding.SetupGet(x => x.CurrentValue).Returns(new EmbeddingOptions());
        var service = new KnowledgeReindexService(database, ingestion.Object, indexStore.Object, embedding.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActivateStagedAsync());
        indexStore.Verify(x => x.ActivateAsync(It.IsAny<KnowledgeIndexDescriptor>(), default), Times.Never);
    }

    [Fact]
    public void Versioned_chunk_identity_is_idempotent_and_partitioned()
    {
        var first = DocumentIngestionService.CreateChunkId("doc", 2, "v2");
        DocumentIngestionService.CreateChunkId("doc", 2, "v2").Should().Be(first);
        DocumentIngestionService.CreateChunkId("doc", 2, "v3").Should().NotBe(first);
    }
}
