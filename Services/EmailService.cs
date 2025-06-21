using ContactFormApi.DTOs;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ContactFormApi.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendContactEmailAsync(ContactFormDto contact)
    {
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_config["EmailSettings:SenderEmail"]));
        email.To.Add(MailboxAddress.Parse(_config["EmailSettings:ReceiverEmail"]));
        email.Subject = $"New Contact Form Submission from {contact.Name}";

        email.Body = new TextPart("html")
        {
            Text = $"""
                <div style="font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f9f9f9; padding: 20px;">
                    <div style="max-width: 600px; margin: auto; background: #ffffff; border: 1px solid #ddd; border-radius: 10px; padding: 20px;">
                        <h2 style="color: #7d2ae8; margin-bottom: 10px;">📩 New Contact Message</h2>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Name:</strong> {contact.Name}</p>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Email:</strong> {contact.Email}</p>
                        <p style="margin: 0 0 10px;"><strong style="color: #333;">Message:</strong></p>
                        <div style="background: #f1f1f1; padding: 12px 15px; border-left: 4px solid #7d2ae8; white-space: pre-wrap; border-radius: 6px; color: #555;">
                            {contact.Message}
                        </div>
                        <p style="margin-top: 20px; font-size: 0.9em; color: #aaa;">Sent on {DateTime.UtcNow:dddd, MMMM d, yyyy h:mm tt} (UTC)</p>
                    </div>
                </div>
            """
        };
        using var smtp = new SmtpClient();
        var smtpServer = Environment.GetEnvironmentVariable("EmailSettings__SmtpServer")
                        ?? _config["EmailSettings:SmtpServer"]
                        ?? throw new InvalidOperationException("EmailSettings:SmtpServer is not configured.");
        var port = Environment.GetEnvironmentVariable("EmailSettings__Port")
                    ?? _config["EmailSettings:Port"]
                    ?? throw new InvalidOperationException("EmailSettings:Port is not configured.");
        var sernderEmail = Environment.GetEnvironmentVariable("EmailSettings__SenderEmail")
                        ?? _config["EmailSettings:SenderEmail"]
                        ?? throw new InvalidOperationException("EmailSettings:SenderEmail is not configured.");
        var password = Environment.GetEnvironmentVariable("EmailSettings__Password")
                        ?? _config["EmailSettings:Password"]
                        ?? throw new InvalidOperationException("EmailSettings:Password is not configured.");
        await smtp.ConnectAsync(smtpServer, int.Parse(port), SecureSocketOptions.SslOnConnect);
        await smtp.AuthenticateAsync(sernderEmail, password);
        await smtp.SendAsync(email);
        await smtp.DisconnectAsync(true);
    }
}