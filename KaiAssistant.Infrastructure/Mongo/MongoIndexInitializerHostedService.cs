using KaiAssistant.Domain.Entities.Outbox;
using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Domain.Entities.Visitors;
using KaiAssistant.Domain.Entities.AI;
using KaiAssistant.Infrastructure.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
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
        var cacheCollection = _database.GetCollection<CachedPrompt>("cached_prompts");
        var knowledgeCollection = _database.GetCollection<KnowledgeDocument>("knowledge_base_documents");
        var conversationCollection = _database.GetCollection<Conversation>("ai_conversations");
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
                new CreateIndexOptions { Name = "ix_outbox_idempotency", Unique = true }),
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys
                    .Ascending(x => x.ProcessedAtUtc)
                    .Ascending(x => x.AttemptCount)
                    .Ascending(x => x.NextAttemptAtUtc),
                new CreateIndexOptions { Name = "ix_outbox_retry_scheduling" })
        };
        var semanticCacheIndexes = new[]
        {
            new CreateIndexModel<CachedPrompt>(
                Builders<CachedPrompt>.IndexKeys.Descending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "ix_cache_created_desc" }),
            new CreateIndexModel<CachedPrompt>(
                Builders<CachedPrompt>.IndexKeys.Ascending(x => x.Prompt),
                new CreateIndexOptions { Name = "ix_cache_prompt" })
        };
        var knowledgeIndexes = new[]
        {
            new CreateIndexModel<KnowledgeDocument>(
                Builders<KnowledgeDocument>.IndexKeys.Ascending(x => x.Source),
                new CreateIndexOptions { Name = "ix_knowledge_source" })
        };
        var conversationIndexes = new[]
        {
            new CreateIndexModel<Conversation>(
                Builders<Conversation>.IndexKeys.Ascending(x => x.ConversationId),
                new CreateIndexOptions { Name = "ix_conversation_id", Unique = true }),
            new CreateIndexModel<Conversation>(
                Builders<Conversation>.IndexKeys.Descending(x => x.UpdatedAt),
                new CreateIndexOptions { Name = "ix_conversation_updated_desc" })
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
        await cacheCollection.Indexes.CreateManyAsync(semanticCacheIndexes, cancellationToken).ConfigureAwait(false);
        await knowledgeCollection.Indexes.CreateManyAsync(knowledgeIndexes, cancellationToken).ConfigureAwait(false);
        await conversationCollection.Indexes.CreateManyAsync(conversationIndexes, cancellationToken).ConfigureAwait(false);
        await modelHealthCollection.Indexes.CreateManyAsync(modelHealthIndexes, cancellationToken).ConfigureAwait(false);
        await visitorCollection.Indexes.CreateManyAsync(visitorIndexes, cancellationToken).ConfigureAwait(false);
        await TryCreateVectorSearchIndexAsync("cached_prompts", "ix_cache_embedding_vector", cancellationToken).ConfigureAwait(false);
        await TryCreateVectorSearchIndexAsync("knowledge_base_documents", "ix_knowledge_embedding_vector", cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Mongo indexes initialized.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private async Task TryCreateVectorSearchIndexAsync(string collection, string indexName, CancellationToken cancellationToken)
    {
        try
        {
            var command = new BsonDocument
            {
                ["createSearchIndexes"] = collection,
                ["indexes"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["name"] = indexName,
                        ["definition"] = new BsonDocument
                        {
                            ["mappings"] = new BsonDocument
                            {
                                ["dynamic"] = false,
                                ["fields"] = new BsonDocument
                                {
                                    ["Embedding"] = new BsonDocument
                                    {
                                        ["type"] = "knnVector",
                                        ["dimensions"] = 64,
                                        ["similarity"] = "cosine"
                                    }
                                }
                            }
                        }
                    }
                }
            };
            await _database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Mongo vector search index ensured. Collection={Collection} Index={IndexName}", collection, indexName);
        }
        catch (MongoCommandException ex)
        {
            _logger.LogWarning(ex, "Vector search index creation skipped for collection {Collection}.", collection);
        }
    }
}