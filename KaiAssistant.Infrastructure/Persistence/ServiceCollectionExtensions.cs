using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Cache;
using KaiAssistant.Infrastructure.Extensions;
using KaiAssistant.Infrastructure.FeatureFlags;
using KaiAssistant.Infrastructure.HostedServices;
using KaiAssistant.Infrastructure.Mongo;
using KaiAssistant.Infrastructure.Observability;
using KaiAssistant.Domain.Interfaces.Repositories;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Infrastructure.EventBus;
using KaiAssistant.Infrastructure.Governance;
using KaiAssistant.Infrastructure.AI;
using KaiAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KaiAssistant.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FeatureFlagsOptions>(configuration.GetSection(FeatureFlagsOptions.SectionName));
        services.Configure<DistributedCacheOptions>(configuration.GetSection(DistributedCacheOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<OutboxProcessorOptions>(configuration.GetSection(OutboxProcessorOptions.SectionName));
        services.Configure<AiGovernanceOptions>(configuration.GetSection(AiGovernanceOptions.SectionName));
        services.Configure<AiModelOrchestrationOptions>(configuration.GetSection(AiModelOrchestrationOptions.SectionName));

        services
            .AddGeminiAiServices(configuration)
            .AddEmailServices(configuration)
            .AddMongo(configuration);

        services.AddSingleton<IFeatureFlagService, ConfigurationFeatureFlagService>();
        services.AddSingleton<IInstanceIdentity, InstanceIdentity>();
        services.AddSingleton<IResilienceStatusProvider, ResilienceStatusProvider>();
        services.TryAddSingleton<IClientContextAccessor, DefaultClientContextAccessor>();
        services.AddSingleton<RedisCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<RedisCacheService>());
        services.AddSingleton<ICacheDiagnosticsService>(sp => sp.GetRequiredService<RedisCacheService>());
        services.AddSingleton<IAiUsageGuard, AiUsageGuard>();
        services.AddSingleton<IModelHealthService, ModelHealthService>();
        services.AddSingleton<IAiTuningState, AiTuningState>();
        services.AddSingleton<IAiDecisionAuditStore, AiDecisionAuditStore>();
        services.AddSingleton<IAiTrafficSimulationService, AiTrafficSimulationService>();
        services.AddSingleton<IModelOrchestrator, ModelOrchestrator>();
        services.AddSingleton<IOutboxProcessorState, OutboxProcessorState>();
        services.AddSingleton<IResumeRepository, Repositories.ResumeRepository>();
        services.AddSingleton<IResumeWriteService, ResumeWriteService>();
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<IEventIdempotencyStore, RedisEventIdempotencyStore>();
        services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();
        services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();

        services.AddHostedService<OutboxProcessorHostedService>();
        services.AddHostedService<MongoIndexInitializerHostedService>();
        services.AddHostedService<ResumeCacheWarmupHostedService>();
        services.AddHostedService<ModelHealthBootstrapHostedService>();
        services.AddHostedService<ModelHealthPersistenceHostedService>();
        services.AddHostedService<AiAutoTuneHostedService>();
        services.AddHostedService<AiTrafficSimulationHostedService>();

        return services;
    }
}