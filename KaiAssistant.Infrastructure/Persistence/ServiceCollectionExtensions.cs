using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Extensions;

namespace KaiAssistant.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
     public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddGeminiAiServices(configuration)
            .AddEmailServices(configuration)
            .AddApplicationServices();

        return services;
    }
}