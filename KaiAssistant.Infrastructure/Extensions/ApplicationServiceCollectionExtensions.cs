using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Application.Contacts.Commands;
using KaiAssistant.Application.AskAssistants.Commands;

namespace KaiAssistant.Infrastructure.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AskAssistantCommand>();
            cfg.RegisterServicesFromAssemblyContaining<ContactCommand>();
        });

        services.AddValidatorsFromAssemblyContaining<AskAssistantCommandValidator>();
        services.AddValidatorsFromAssemblyContaining<ContactCommandValidator>();

        return services;
    }
}
