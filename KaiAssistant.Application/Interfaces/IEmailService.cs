namespace KaiAssistant.Application.Services;

public interface IEmailService
{
    Task SendContactEmailAsync(string Name, string Email, string Message);
    Task SendConfirmationEmailAsync(string Name, string Email);
}