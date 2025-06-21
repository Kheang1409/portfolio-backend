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
        var smtpServer = Environment.GetEnvironmentVariable("EmailSettings__SmtpServer")
                        ?? _config["EmailSettings:SmtpServer"]
                        ?? throw new InvalidOperationException("EmailSettings:SmtpServer is not configured.");
        var senderEmail = Environment.GetEnvironmentVariable("EmailSettings__SenderEmail")
                        ?? _config["EmailSettings:SenderEmail"]
                        ?? throw new InvalidOperationException("EmailSettings:SenderEmail is not configured.");
        var port = Environment.GetEnvironmentVariable("EmailSettings__Port")
                    ?? _config["EmailSettings:Port"]
                    ?? throw new InvalidOperationException("EmailSettings:Port is not configured.");
        var password = Environment.GetEnvironmentVariable("EmailSettings__Password")
                        ?? _config["EmailSettings:Password"]
                        ?? throw new InvalidOperationException("EmailSettings:Password is not configured.");
        var receiverEmail = Environment.GetEnvironmentVariable("EmailSettings__ReceiverEmail")
                        ?? _config["EmailSettings:ReceiverEmail"]
                        ?? throw new InvalidOperationException("EmailSettings:ReceiverEmail is not configured.");

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(smtpServer, int.Parse(port), SecureSocketOptions.SslOnConnect);
        await smtp.AuthenticateAsync(senderEmail, password);

        // 1️⃣ Send to YOU
        var adminEmail = new MimeMessage();
        adminEmail.From.Add(MailboxAddress.Parse(senderEmail));
        adminEmail.To.Add(MailboxAddress.Parse(receiverEmail));
        adminEmail.Subject = $"New Contact Form Submission from {contact.Name}";
        adminEmail.Body = new TextPart("html")
        {
            Text = $"""
                <div style="font-family: 'Segoe UI', sans-serif; background-color: #f9f9f9; padding: 20px;">
                    <div style="max-width: 600px; margin: auto; background: #ffffff; border: 1px solid #ddd; border-radius: 10px; padding: 20px;">
                        <h2 style="color: #7d2ae8;">📩 New Contact Message</h2>
                        <p><strong>Name:</strong> {contact.Name}</p>
                        <p><strong>Email:</strong> {contact.Email}</p>
                        <p><strong>Message:</strong></p>
                        <div style="background: #f1f1f1; padding: 12px; border-left: 4px solid #7d2ae8; border-radius: 6px; color: #555;">
                            {contact.Message}
                        </div>
                        <p style="margin-top: 20px; font-size: 0.9em; color: #aaa;">Sent on {DateTime.UtcNow:dddd, MMMM d, yyyy h:mm tt} (UTC)</p>
                    </div>
                </div>
            """
        };
        await smtp.SendAsync(adminEmail);

        // 2️⃣ Send to USER
        var userEmail = new MimeMessage();
        userEmail.From.Add(MailboxAddress.Parse(senderEmail));
        userEmail.To.Add(MailboxAddress.Parse(contact.Email));
        userEmail.Subject = "Thanks for contacting me!";
        userEmail.Body = new TextPart("html")
        {
            Text = $"""
                <div style="font-family: 'Segoe UI', sans-serif; background-color: #f9f9f9; padding: 20px;">
                    <div style="max-width: 600px; margin: auto; background: #ffffff; border: 1px solid #ddd; border-radius: 10px; padding: 20px;">
                        <h2 style="color: #7d2ae8;">👋 Hi {contact.Name},</h2>
                        <p>Thanks for reaching out! I’ve received your message and will get back to you as soon as possible.</p>
                        <p>Here’s a copy of what you sent:</p>
                        <div style="background: #f1f1f1; padding: 12px; border-left: 4px solid #7d2ae8; border-radius: 6px; color: #555;">
                            {contact.Message}
                        </div>
                        <p style="margin-top: 20px;">Have a great day!<br><strong>- Kai Taing</strong></p>
                    </div>
                </div>
            """
        };
        await smtp.SendAsync(userEmail);

        await smtp.DisconnectAsync(true);
    }

}