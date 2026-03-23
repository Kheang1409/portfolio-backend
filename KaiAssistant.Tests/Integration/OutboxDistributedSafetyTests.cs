using FluentAssertions;
using KaiAssistant.Application.Events;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities.Outbox;
using KaiAssistant.Infrastructure.EventBus;
using KaiAssistant.Infrastructure.Mongo;
using KaiAssistant.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Mongo2Go;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;
using Xunit;

namespace KaiAssistant.Tests.Integration;

[Trait("Category", "Integration")]
public class OutboxDistributedSafetyTests
{
    [Fact]
    public async Task LeasePendingAsync_ShouldOnlyAllowSingleLeaseAcrossInstances()
    {
        MongoClassMapRegistrar.RegisterClassMaps();
        using var runner = MongoDbRunner.Start();
        var client = new MongoClient(runner.ConnectionString);
        var db = client.GetDatabase("outbox_lease_test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new MongoSettings { ConnectionString = runner.ConnectionString, DatabaseName = "outbox_lease_test" });
        services.AddSingleton<IMongoClient>(client);
        services.AddSingleton<IMongoDatabase>(db);
        services.AddSingleton<IMongoReadProvider, MongoReadProvider>();
        services.AddSingleton<IMongoWriteProvider, MongoWriteProvider>();
        services.AddSingleton<IOutboxRepository, OutboxRepository>();

        var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        var evt = new ResumeCreatedIntegrationEvent("resume-1")
        {
            EventId = Guid.NewGuid(),
            IdempotencyKey = "idem-lease"
        };

        var msg = OutboxMessageFactory.Create(evt);
        await repository.AddAsync(msg);

        var now = DateTimeOffset.UtcNow;
        var leaseA = await repository.LeasePendingAsync(10, now, TimeSpan.FromSeconds(30), "instance-a");
        var leaseB = await repository.LeasePendingAsync(10, now, TimeSpan.FromSeconds(30), "instance-b");

        leaseA.Should().HaveCount(1);
        leaseB.Should().BeEmpty();
    }

    [Fact]
    public async Task RedisEventIdempotencyStore_ShouldPreventDuplicateProcessing()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton<IEventIdempotencyStore, RedisEventIdempotencyStore>();

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IEventIdempotencyStore>();

        var key = "idem-test-key";

        var first = await store.TryAcquireAsync(key, TimeSpan.FromSeconds(30));
        first.Should().Be(IdempotencyAcquireResult.Acquired);

        await store.MarkProcessedAsync(key, TimeSpan.FromMinutes(10));

        var second = await store.TryAcquireAsync(key, TimeSpan.FromSeconds(30));
        second.Should().Be(IdempotencyAcquireResult.AlreadyProcessed);
    }

    [Fact]
    public async Task MarkProcessedAsync_ShouldRejectWrongLockOwner()
    {
        MongoClassMapRegistrar.RegisterClassMaps();
        using var runner = MongoDbRunner.Start();
        var client = new MongoClient(runner.ConnectionString);
        var db = client.GetDatabase("outbox_lock_owner_test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new MongoSettings { ConnectionString = runner.ConnectionString, DatabaseName = "outbox_lock_owner_test" });
        services.AddSingleton<IMongoClient>(client);
        services.AddSingleton<IMongoDatabase>(db);
        services.AddSingleton<IMongoReadProvider, MongoReadProvider>();
        services.AddSingleton<IMongoWriteProvider, MongoWriteProvider>();
        services.AddSingleton<IOutboxRepository, OutboxRepository>();

        var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IOutboxRepository>();

        var evt = new ResumeCreatedIntegrationEvent("resume-2")
        {
            EventId = Guid.NewGuid(),
            IdempotencyKey = "idem-owner"
        };

        var msg = OutboxMessageFactory.Create(evt);
        await repository.AddAsync(msg);

        var leased = await repository.LeasePendingAsync(1, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30), "instance-a");
        leased.Should().HaveCount(1);

        await repository.MarkProcessedAsync(leased[0].Id!, DateTimeOffset.UtcNow, "instance-b");

        var reloaded = await repository.GetByIdAsync(leased[0].Id!);
        reloaded.Should().NotBeNull();
        reloaded!.ProcessedAtUtc.Should().BeNull();
    }
}
