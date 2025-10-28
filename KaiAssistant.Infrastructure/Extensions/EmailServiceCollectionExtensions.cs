using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Infrastructure.Services;
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
        string recieverEmail = Environment.GetEnvironmentVariable("SMTP_RECIEVER_EMAIL")
                            ?? emailSettingsSection["RecieverEmail"]
                            ?? throw new ArgumentException("Email setting 'RecieverEmail' is missing or empty.");
        string senderPassword = Environment.GetEnvironmentVariable("SMTP_SENDER_PASSWORD")
                            ?? emailSettingsSection["SenderPassword"]
                            ?? string.Empty;

        bool enabled;
        var envEnabled = Environment.GetEnvironmentVariable("SMTP_ENABLED");
        if (!string.IsNullOrWhiteSpace(envEnabled) && bool.TryParse(envEnabled, out var parsedEnabled))
        {
            enabled = parsedEnabled;
        }
        else if (!string.IsNullOrWhiteSpace(emailSettingsSection["Enabled"]) && bool.TryParse(emailSettingsSection["Enabled"], out var parsedFromConfig))
        {
            enabled = parsedFromConfig;
        }
        else
        {
            // Default to disabled in Development to avoid noisy SMTP errors while iterating locally
            var aspnetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            enabled = !string.Equals(aspnetEnv, "Development", StringComparison.OrdinalIgnoreCase);
        }

        var emailSettings = new EmailSettings(smtpServer, int.Parse(portString), senderEmail, recieverEmail, senderPassword, enabled);
        services.AddSingleton(emailSettings);
    services.AddSingleton<KaiAssistant.Application.Services.IEmailService, EmailService>();
        return services;
    }
}
