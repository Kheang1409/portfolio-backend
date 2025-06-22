using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using KaiAssistant.Domain.Entities;

namespace KaiAssistant.Application.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;

    public EmailService(EmailSettings emailSettings)
    {
        _emailSettings = emailSettings;
    }

    public async Task SendContactEmailAsync(string Name, string Email, string Message)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_emailSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(_emailSettings.RecieverEmail));
        message.Subject = $"New Contact Form Submission from {Name}";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <div style="font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f9f9f9; padding: 20px;">
                    <div style="max-width: 600px; margin: auto; background: #ffffff; border: 1px solid #ddd; border-radius: 10px; padding: 20px;">
                        <h2 style="color: #7d2ae8; margin-bottom: 10px;">📩 New Contact Message</h2>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Name:</strong> {Name}</p>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Email:</strong> {Email}</p>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Message:</strong></p>
                        <div style="background: #f1f1f1; padding: 12px 15px; border-left: 4px solid #7d2ae8; white-space: pre-wrap; border-radius: 6px; color: #555;">
                            {Message}
                        </div>
                        <p style="margin-top: 20px; font-size: 0.9em; color: #aaa;">Sent on {DateTime.UtcNow:dddd, MMMM d, yyyy h:mm tt} (UTC)</p>
                    </div>
                </div>
            """
        };

        await SendEmailAsync(message);
    }
    
    private async Task SendEmailAsync(MimeMessage message)
    {
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.Port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception)
        {
            throw;
        }
    }
}