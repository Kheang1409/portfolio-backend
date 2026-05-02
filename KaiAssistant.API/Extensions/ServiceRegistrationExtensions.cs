using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Services;
using KaiAssistant.Infrastructure.Persistence;
using KaiAssistant.Infrastructure.Services;
using StackExchange.Redis;
namespace KaiAssistant.API.Extensions;
public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddAiOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Infrastructure Services
        services.AddSingleton<IVisitDeduplicationService, VisitDeduplicationService>();
        services.AddSingleton<IResponseCacheService, ResponseCacheService>();
        services.AddSingleton<IRateLimitingService, RateLimitingService>();
        services.AddSingleton<ITokenBudgetService, TokenBudgetService>();
        // Application Services
        services.AddScoped<IAssistantContextBuilder, AssistantContextBuilder>();
        // Register base orchestrator concrete type so enhanced orchestrator can decorate it.
        services.AddScoped<AssistantOrchestrator>();
        // Default to enhanced orchestrator if available; otherwise use base concrete orchestrator.
        services.AddScoped<IAssistantOrchestrator>(sp =>
        {
            var enhanced = sp.GetService<IEnhancedAssistantOrchestrator>();
            if (enhanced != null)
                return (IAssistantOrchestrator)enhanced;
            return sp.GetRequiredService<AssistantOrchestrator>();
        });
        // Persistence
        services.AddScoped<IConversationRepository, ConversationRepository>();
        return services;
    }
}