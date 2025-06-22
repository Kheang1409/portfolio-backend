using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace KaiAssistant.Infrastructure.Extensions;

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        var emailSettingsSection = configuration.GetSection("EmailSettings");
        
        string smtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER")
                            ?? emailSettingsSection["SmtpServer"]
                            ?? throw new ArgumentException("Email setting 'SmtpServer' is missing or empty.");
        string portString = Environment.GetEnvironmentVariable("SMTP_PORT")
                            ?? emailSettingsSection["Port"]
                            ?? throw new ArgumentException("Email setting 'Port' is missing or not a valid integer.");
        string senderEmail = Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL")
                            ?? emailSettingsSection["SenderEmail"]
                            ?? throw new ArgumentException("Email setting 'SenderEmail' is missing or empty.");
        string recieverEmail = Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL")
                            ?? emailSettingsSection["RecieverEmail"]
                            ?? throw new ArgumentException("Email setting 'RecieverEmail' is missing or empty.");
        string senderPassword = Environment.GetEnvironmentVariable("SMTP_SENDER_PASSWORD")
                            ?? emailSettingsSection["SenderPassword"]
                            ?? throw new ArgumentException("Email setting 'SenderPassword' is missing or empty.");

        var emailSettings = new EmailSettings(smtpServer, int.Parse(portString), senderEmail, recieverEmail, senderPassword);
        services.AddSingleton(emailSettings);
        services.AddSingleton<IEmailService, EmailService>();
        return services;
    }
}
