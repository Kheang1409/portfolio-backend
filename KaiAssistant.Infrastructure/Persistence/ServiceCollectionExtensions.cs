using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Extensions;
using KaiAssistant.Application.Extensions;

namespace KaiAssistant.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddGeminiAiServices(configuration)
            .AddEmailServices(configuration)
            .AddApplicationServices()
            .AddMongo(configuration);

    services.AddSingleton<KaiAssistant.Domain.Interfaces.Repositories.IResumeRepository, KaiAssistant.Infrastructure.Persistence.Repositories.ResumeRepository>();

        return services;
    }
}