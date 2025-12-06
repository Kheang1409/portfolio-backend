using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Infrastructure.Extensions;
using KaiAssistant.Domain.Interfaces.Repositories;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.Persistence.Repositories;
using KaiAssistant.Application.Extensions;

namespace KaiAssistant.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddApplicationServices()
            .AddGeminiAiServices(configuration)
            .AddEmailServices(configuration)
            .AddMongo(configuration);

        services.AddSingleton<IResumeRepository, Repositories.ResumeRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();

        return services;
    }
}