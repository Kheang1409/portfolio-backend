namespace KaiAssistant.Application.Services;
public interface IEmailService
{
    Task SendContactEmailAsync(string Name, string Email, string Message, CancellationToken cancellationToken = default);
    Task SendConfirmationEmailAsync(string Name, string Email, CancellationToken cancellationToken = default);
}