using KaiAssistant.Domain.Entities.Outbox;
using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Domain.Entities.Visitors;
using KaiAssistant.Infrastructure.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace KaiAssistant.Infrastructure.Mongo;

public sealed class MongoIndexInitializerHostedService : IHostedService
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoIndexInitializerHostedService> _logger;

    public MongoIndexInitializerHostedService(
        IMongoDatabase database,
        ILogger<MongoIndexInitializerHostedService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var resumeCollection = _database.GetCollection<Resume>("resumes");
        var outboxCollection = _database.GetCollection<OutboxMessage>("outbox_messages");
        var visitorCollection = _database.GetCollection<VisitorEvent>("visitorevents");
        var modelHealthCollection = _database.GetCollection<ModelHealthStateDocument>("assistant_model_health");

        var resumeIndexes = new[]
        {
            new CreateIndexModel<Resume>(
                Builders<Resume>.IndexKeys
                    .Descending(x => x.CreatedAtUtc)
                    .Descending(x => x.Id),
                new CreateIndexOptions { Name = "ix_resume_created_desc" })
        };

        var outboxIndexes = new[]
        {
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys.Ascending(x => x.EventId),
                new CreateIndexOptions { Name = "ix_outbox_event_id", Unique = true }),
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys
                    .Ascending(x => x.ProcessedAtUtc)
                    .Ascending(x => x.NextAttemptAtUtc),
                new CreateIndexOptions { Name = "ix_outbox_pending" }),
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys
                    .Ascending(x => x.ProcessedAtUtc)
                    .Ascending(x => x.LockExpiresAtUtc)
                    .Ascending(x => x.NextAttemptAtUtc),
                new CreateIndexOptions { Name = "ix_outbox_leasing" }),
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys.Ascending(x => x.IdempotencyKey),
                new CreateIndexOptions { Name = "ix_outbox_idempotency", Unique = true })
        };

        var modelHealthIndexes = new[]
        {
            new CreateIndexModel<ModelHealthStateDocument>(
                Builders<ModelHealthStateDocument>.IndexKeys.Ascending(x => x.ModelName),
                new CreateIndexOptions { Name = "ix_model_health_model_name", Unique = true }),
            new CreateIndexModel<ModelHealthStateDocument>(
                Builders<ModelHealthStateDocument>.IndexKeys.Ascending(x => x.CircuitOpenUntilUtc),
                new CreateIndexOptions { Name = "ix_model_health_circuit_open_until" }),
            new CreateIndexModel<ModelHealthStateDocument>(
                Builders<ModelHealthStateDocument>.IndexKeys.Descending(x => x.UpdatedAtUtc),
                new CreateIndexOptions { Name = "ix_model_health_updated_desc" }),
            new CreateIndexModel<ModelHealthStateDocument>(
                Builders<ModelHealthStateDocument>.IndexKeys.Descending(x => x.DynamicScore),
                new CreateIndexOptions { Name = "ix_model_health_dynamic_score_desc" }),
            new CreateIndexModel<ModelHealthStateDocument>(
                Builders<ModelHealthStateDocument>.IndexKeys.Descending(x => x.TotalEstimatedCostUsd),
                new CreateIndexOptions { Name = "ix_model_health_total_cost_desc" })
        };

        var visitorIndexes = new[]
        {
            new CreateIndexModel<VisitorEvent>(
                Builders<VisitorEvent>.IndexKeys.Descending(x => x.VisitedAtUtc),
                new CreateIndexOptions { Name = "ix_visits_visited_at_desc" }),
            new CreateIndexModel<VisitorEvent>(
                Builders<VisitorEvent>.IndexKeys.Ascending(x => x.SessionId),
                new CreateIndexOptions { Name = "ix_visits_session_id" }),
            new CreateIndexModel<VisitorEvent>(
                Builders<VisitorEvent>.IndexKeys.Ascending(x => x.Path),
                new CreateIndexOptions { Name = "ix_visits_path" })
        };

        await resumeCollection.Indexes.CreateManyAsync(resumeIndexes, cancellationToken).ConfigureAwait(false);
        await outboxCollection.Indexes.CreateManyAsync(outboxIndexes, cancellationToken).ConfigureAwait(false);
        await modelHealthCollection.Indexes.CreateManyAsync(modelHealthIndexes, cancellationToken).ConfigureAwait(false);
        await visitorCollection.Indexes.CreateManyAsync(visitorIndexes, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Mongo indexes initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
