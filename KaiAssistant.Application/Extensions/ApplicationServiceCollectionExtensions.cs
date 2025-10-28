using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Application.Contacts.Commands;
using KaiAssistant.Application.AskAssistants.Commands;
using KaiAssistant.Application.Resumes.Commands;
using KaiAssistant.Application.Resumes.Queries;

namespace KaiAssistant.Application.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AskAssistantCommand>();
            cfg.RegisterServicesFromAssemblyContaining<ContactCommand>();
            cfg.RegisterServicesFromAssemblyContaining<CreateResumeCommand>();
            cfg.RegisterServicesFromAssemblyContaining<GetLatestResumeQuery>();
            cfg.RegisterServicesFromAssemblyContaining<GetResumeByIdQuery>();
        });
        services.AddValidatorsFromAssemblyContaining<AskAssistantCommandValidator>();
        services.AddValidatorsFromAssemblyContaining<ContactCommandValidator>();
        return services;
    }
}
