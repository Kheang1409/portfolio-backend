namespace KaiAssistant.Infrastructure.Extensions;
using global::KaiAssistant.Application.Interfaces;
using global::KaiAssistant.Application.Services;
using global::KaiAssistant.Application.Services.Tools;
using global::KaiAssistant.Infrastructure.Resilience;
using global::KaiAssistant.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Linq;
public static class AdvancedPlatformExtensions
{
    public static IServiceCollection AddAdvancedAiPlatform(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ========== PART 1: TOOL CALLING SYSTEM ==========
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        // Register example tools
        services.AddScoped<GetPortfolioProjectsTool>();
        services.AddScoped<GetResumeTool>();
        services.AddScoped<GetSystemStatusTool>();
        // ========== PART 2: SEMANTIC SEARCH & RAG ==========
        // Offline fallback for hosts that intentionally do not register production infrastructure.
        if (!services.Any(x => x.ServiceType == typeof(IEmbeddingService)))
        {
            services.AddSingleton<IEmbeddingService, DeterministicTestEmbeddingService>();
        }
        // Semantic search service
        services.AddScoped<ISemanticSearchService, InMemorySemanticSearchService>();
        // Enhanced context builder with RAG
        services.AddScoped<EnhancedContextBuilder>();
        // ========== PART 3: STREAMING ENHANCEMENTS ==========
        // Enhanced streaming models already in DTOs/StreamingEnhancements.cs
        // No additional registration needed - types are already available
        // ========== PART 4: COST + PERFORMANCE HARDENING ==========
        // Cache enhancement with normalization & versioning
        services.AddEnhancedCaching(configuration);
        // Resilience policies (circuit breaker, retry, timeout)
        services.AddAiResilience(configuration);
        // ========== PART 5: CONVERSATION INTELLIGENCE ==========
        services.AddScoped<IConversationSummaryService, ConversationSummaryService>();
        // ========== PART 6: VISIT TRACKING ==========
        services.AddSingleton<IVisitTrackingService, RedisVisitTrackingService>();
        // ========== PART 7: OBSERVABILITY ==========
        services.AddSingleton<IObservabilityService, ObservabilityService>();
        // ========== INTEGRATION: ENHANCED ORCHESTRATOR ==========
        // Register enhanced orchestrator as decorator
        services.AddScoped<IEnhancedAssistantOrchestrator, EnhancedAssistantOrchestrator>();
        return services;
    }
    public static IHttpClientBuilder AddGeminiWithResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get policy registry
        var policyRegistry = services.AddPolicyRegistry();
        // Configure comprehensive resilience policy
        policyRegistry.Add("gemini-comprehensive", AiResiliencePolicies.CreateComprehensivePolicy());
        // Build HTTP client with all policies
        return services
            .AddHttpClient("GeminiWithResilience", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "KaiAssistant/1.0");
            })
            .AddPolicyHandlerFromRegistry("gemini-comprehensive");
    }
}
public static class ProgramIntegrationExample
{
    public static void ExampleServiceConfiguration(IServiceCollection services, IConfiguration config)
    {
        // Existing setup...
        // services.AddInfrastructure(config);
        // services.AddApplicationServices();
        // Add advanced platform features
        // services.AddAdvancedAiPlatform(config);
        // Add Gemini with resilience
        // services.AddGeminiWithResilience(config);
    }
}
