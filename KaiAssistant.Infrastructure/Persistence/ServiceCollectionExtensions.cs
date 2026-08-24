using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Cache;
using KaiAssistant.Infrastructure.Extensions;
using KaiAssistant.Infrastructure.HostedServices;
using KaiAssistant.Infrastructure.Mongo;
using KaiAssistant.Infrastructure.Observability;
using KaiAssistant.Domain.Interfaces.Repositories;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Application.Services;
using KaiAssistant.Infrastructure.Governance;
using KaiAssistant.Infrastructure.AI;
using KaiAssistant.Infrastructure.AI.Caching;
using KaiAssistant.Infrastructure.AI.Conversation;
using KaiAssistant.Infrastructure.AI.Providers;
using KaiAssistant.Infrastructure.AI.Rag;
using KaiAssistant.Infrastructure.AI.Embeddings;
using KaiAssistant.Infrastructure.FeatureFlags;
using KaiAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace KaiAssistant.Infrastructure.Persistence;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.Configure<AiGovernanceOptions>(configuration.GetSection(AiGovernanceOptions.SectionName));
        services.Configure<FeatureFlagsOptions>(configuration.GetSection(FeatureFlagsOptions.SectionName));
        services.Configure<AiModelOrchestrationOptions>(configuration.GetSection(AiModelOrchestrationOptions.SectionName));
        services.Configure<SemanticCacheOptions>(configuration.GetSection(SemanticCacheOptions.SectionName));
        services.Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName));
        services.Configure<KnowledgeOptions>(configuration.GetSection(KnowledgeOptions.SectionName));
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.Configure<ConversationOptions>(configuration.GetSection(ConversationOptions.SectionName));
        services
            .AddGeminiAiServices(configuration)
            .AddEmailServices(configuration)
            .AddMongo(configuration);
        services.AddSingleton<IFeatureFlagService, DynamicFeatureFlagService>();
        services.AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();
        services.AddSingleton<RedisExecutionHelper>();
        services.AddSingleton<IResilienceStatusProvider, ResilienceStatusProvider>();
        services.AddSingleton<IInstanceIdentity, InstanceIdentity>();
        services.TryAddSingleton<IClientContextAccessor, DefaultClientContextAccessor>();
        services.AddSingleton<RedisCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<RedisCacheService>());
        services.AddSingleton<ICacheDiagnosticsService>(sp => sp.GetRequiredService<RedisCacheService>());
        services.AddSingleton<IAiUsageGuard, AiUsageGuard>();
        services.AddSingleton<IPromptSecurityService, PromptSecurityService>();
        services.AddHttpClient("GeminiEmbeddings", client => client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"));
        services.AddSingleton<IEmbeddingService, GeminiEmbeddingService>();
        services.AddScoped<ISemanticCacheService, SemanticCacheService>();
        services.AddScoped<IRagService, RagService>();
        services.AddScoped<IVectorStore, MongoVectorStore>();
        services.AddScoped<IKeywordSearch, MongoKeywordSearch>();
        services.AddScoped<IHybridRetriever, HybridRetriever>();
        services.AddSingleton<IRagContextSelector, RagContextSelector>();
        services.AddSingleton<IKnowledgeIndexStore, MongoKnowledgeIndexStore>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IAiEvaluationService, AiEvaluationService>();
        services.AddScoped<IAiProvider, GeminiProvider>();
        services.AddScoped<IAiOrchestratorService, AiOrchestratorService>();
        services.AddScoped<IAssistantService, AssistantService>();
        // GeminiGateway uses this lightweight in-process circuit state; it is not model autotuning.
        services.AddSingleton<IModelHealthService, ModelHealthService>();
        services.AddSingleton<IModelOrchestrator, ModelOrchestrator>();
        services.AddScoped<IResumeWriteService, ResumeWriteService>();
        services.AddSingleton<IResumeRepository, Repositories.ResumeRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddHostedService<MongoIndexInitializerHostedService>();
        services.AddHostedService<KnowledgeBootstrapHostedService>();
        return services;
    }
}
