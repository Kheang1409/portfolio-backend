namespace KaiAssistant.Application.Services;

public interface IEmailService
{
    Task SendContactEmailAsync(string Name, string Email, string Message);
}