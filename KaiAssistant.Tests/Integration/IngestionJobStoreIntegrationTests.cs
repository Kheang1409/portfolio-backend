using KaiAssistant.Domain.Entities.AI;
using KaiAssistant.Infrastructure.AI.Rag;
using MongoDB.Driver;
using Xunit;
using System.Threading.Tasks;
using System;

namespace KaiAssistant.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class IngestionJobStoreIntegrationTests
{
    [Fact]
    public async Task ClaimIsAtomicAndWrongWorkerCannotRenew()
    {
        using var runner = MongoTestRunner.Start();
        var store = new MongoIngestionJobStore(new MongoClient(runner.ConnectionString).GetDatabase("job_claim_test"));
        var job = new IngestionJob { Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), DocumentId = "document-1", MaxAttempts = 3 };
        await store.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var first = await store.ClaimAsync("worker-a", now, TimeSpan.FromMinutes(1));
        var second = await store.ClaimAsync("worker-b", now, TimeSpan.FromMinutes(1));
        Assert.NotNull(first);
        Assert.Null(second);
        Assert.False(await store.RenewLeaseAsync(job.Id!, "worker-b", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.RenewLeaseAsync(job.Id!, "worker-a", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task ExpiredLeaseCanBeRecoveredByAnotherWorker()
    {
        using var runner = MongoTestRunner.Start();
        var store = new MongoIngestionJobStore(new MongoClient(runner.ConnectionString).GetDatabase("job_recovery_test"));
        var job = new IngestionJob { Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), DocumentId = "document-2" };
        await store.AddAsync(job);
        var claimed = await store.ClaimAsync("worker-a", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        var recovered = await store.ClaimAsync("worker-b", DateTimeOffset.UtcNow.AddSeconds(2), TimeSpan.FromMinutes(1));
        Assert.Equal("worker-a", claimed!.WorkerId);
        Assert.Equal("worker-b", recovered!.WorkerId);
        await store.CompleteAsync(job.Id!, "worker-b");
        Assert.Equal(IngestionJobStatus.Completed, (await store.GetAsync(job.Id!))!.Status);
    }
}
