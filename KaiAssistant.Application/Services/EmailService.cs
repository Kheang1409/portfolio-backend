using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Net;
using System.Text;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace KaiAssistant.Application.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly IResilienceStatusProvider _resilience;
    private readonly ILogger<EmailService> _logger;
    private readonly AsyncPolicy _sendPolicy;

    public EmailService(EmailSettings emailSettings, IResilienceStatusProvider resilience, ILogger<EmailService> logger)
    {
        _emailSettings = emailSettings;
        _resilience = resilience;
        _logger = logger;

        var retry = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(Math.Min(2000, 200 * Math.Pow(2, attempt))));

        var timeout = Policy.TimeoutAsync(TimeSpan.FromSeconds(10));
        var circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                5,
                TimeSpan.FromSeconds(30),
                (ex, breakDelay) =>
                {
                    _resilience.RecordFailure("email", ex.Message);
                    _resilience.RecordCircuitState("email", "Open");
                    _logger.LogWarning(ex, "Email circuit opened for {BreakDelay}.", breakDelay);
                },
                () =>
                {
                    _resilience.RecordCircuitState("email", "Closed");
                    _logger.LogInformation("Email circuit reset.");
                },
                () => _resilience.RecordCircuitState("email", "HalfOpen"));

        _sendPolicy = Policy.WrapAsync(retry, timeout, circuitBreaker);
    }

    public async Task SendContactEmailAsync(string name, string email, string messageText, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_emailSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(_emailSettings.RecieverEmail));
        message.Subject = $"New portfolio contact: {name}";

        var builder = new BodyBuilder
        {
            HtmlBody = BuildOwnerNotificationHtml(name, email, messageText, utcNow),
            TextBody = BuildOwnerNotificationText(name, email, messageText, utcNow)
        };
        message.Body = builder.ToMessageBody();

        await SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendConfirmationEmailAsync(string name, string email, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_emailSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Thanks for reaching out — I received your message";

        var builder = new BodyBuilder
        {
            HtmlBody = BuildAutoReplyHtml(name, utcNow),
            TextBody = BuildAutoReplyText(name, utcNow)
        };
        message.Body = builder.ToMessageBody();

        await SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
    }
    
    private async Task SendEmailAsync(MimeMessage message, CancellationToken cancellationToken)
    {
            await _sendPolicy.ExecuteAsync(async ct =>
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.Port, SecureSocketOptions.Auto, ct).ConfigureAwait(false);
                await client.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword, ct).ConfigureAwait(false);
                await client.SendAsync(message, ct).ConfigureAwait(false);
                await client.DisconnectAsync(true, ct).ConfigureAwait(false);
                _resilience.RecordSuccess("email");
            }, cancellationToken).ConfigureAwait(false);
    }

        private static string BuildOwnerNotificationHtml(string name, string email, string messageText, DateTime utcNow)
        {
                var safeName = HtmlEncode(name);
                var safeEmail = HtmlEncode(email);
                var safeMessage = HtmlEncode(messageText);

                return $"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>New Contact Message</title>
</head>
<body style="margin:0; padding:0; background-color:#f8fafc; font-family: 'Segoe UI', Tahoma, Arial, sans-serif; color:#0f172a;">
    <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="padding:32px 16px; background-color:#f8fafc;">
        <tr>
            <td align="center">
                <table width="640" cellpadding="0" cellspacing="0" role="presentation" style="max-width:640px; width:100%; background-color:#ffffff; border:1px solid #e2e8f0; border-radius:14px; overflow:hidden;">
                    <tr>
                        <td style="padding:24px 28px; background-color:#0b1220;">
                            <div style="font-size:12px; letter-spacing:.08em; text-transform:uppercase; color:#cbd5e1;">KaiAssistant • Portfolio Contact</div>
                            <div style="margin-top:6px; font-size:22px; font-weight:700; color:#ffffff;">New contact message</div>
                            <div style="margin-top:6px; font-size:13px; color:#94a3b8;">Received {utcNow:yyyy-MM-dd HH:mm} UTC</div>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:24px 28px;">
                            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:0 0 18px;">
                                <tr>
                                    <td style="padding:14px 16px; background-color:#f1f5f9; border:1px solid #e2e8f0; border-radius:12px;">
                                        <div style="font-size:12px; color:#475569; text-transform:uppercase; letter-spacing:.06em;">From</div>
                                        <div style="margin-top:4px; font-size:18px; font-weight:700; color:#0f172a;">{safeName}</div>
                                        <div style="margin-top:6px; font-size:14px; color:#334155;">
                                            <a href="mailto:{safeEmail}" style="color:#2563eb; text-decoration:none;">{safeEmail}</a>
                                        </div>
                                    </td>
                                </tr>
                            </table>

                            <div style="font-size:12px; color:#475569; text-transform:uppercase; letter-spacing:.06em;">Message</div>
                            <div style="margin-top:8px; padding:16px; background-color:#ffffff; border:1px solid #e2e8f0; border-radius:12px;">
                                <div style="white-space:pre-wrap; word-break:break-word; font-size:14px; line-height:1.7; color:#0f172a;">{safeMessage}</div>
                            </div>

                            <div style="margin-top:20px;">
                                <a href="mailto:{safeEmail}?subject=Re:%20Portfolio%20contact" style="display:inline-block; background-color:#2563eb; color:#ffffff; text-decoration:none; padding:12px 18px; border-radius:10px; font-weight:700; font-size:14px;">Reply</a>
                                <span style="margin-left:10px; font-size:12px; color:#64748b;">Tip: reply directly to the sender’s email.</span>
                            </div>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:16px 28px; background-color:#f8fafc; border-top:1px solid #e2e8f0;">
                            <div style="font-size:12px; color:#64748b; line-height:1.6;">This email was generated automatically from your portfolio contact form.</div>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
        }

        private static string BuildOwnerNotificationText(string name, string email, string messageText, DateTime utcNow)
        {
                var sb = new StringBuilder();
                sb.AppendLine("New contact message (Portfolio)");
                sb.AppendLine($"Received: {utcNow:yyyy-MM-dd HH:mm} UTC");
                sb.AppendLine();
                sb.AppendLine($"From: {name}");
                sb.AppendLine($"Email: {email}");
                sb.AppendLine();
                sb.AppendLine("Message:");
                sb.AppendLine(messageText);
                return sb.ToString();
        }

        private static string BuildAutoReplyHtml(string name, DateTime utcNow)
        {
                var safeName = HtmlEncode(name);

                return $"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Message Received</title>
</head>
<body style="margin:0; padding:0; background-color:#f8fafc; font-family: 'Segoe UI', Tahoma, Arial, sans-serif; color:#0f172a;">
    <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="padding:32px 16px; background-color:#f8fafc;">
        <tr>
            <td align="center">
                <table width="640" cellpadding="0" cellspacing="0" role="presentation" style="max-width:640px; width:100%; background-color:#ffffff; border:1px solid #e2e8f0; border-radius:14px; overflow:hidden;">
                    <tr>
                        <td style="padding:24px 28px; background-color:#0b1220;">
                            <div style="font-size:12px; letter-spacing:.08em; text-transform:uppercase; color:#cbd5e1;">Hang Kheang Taing • Software Engineer</div>
                            <div style="margin-top:6px; font-size:22px; font-weight:700; color:#ffffff;">Thanks — I received your message</div>
                            <div style="margin-top:6px; font-size:13px; color:#94a3b8;">Auto-reply sent {utcNow:yyyy-MM-dd HH:mm} UTC</div>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:24px 28px;">
                            <p style="margin:0 0 14px; font-size:15px; line-height:1.7; color:#0f172a;">Hi <strong>{safeName}</strong>,</p>
                            <p style="margin:0 0 14px; font-size:14px; line-height:1.7; color:#334155;">Thank you for reaching out through my portfolio. I’ve received your message and will review it shortly.</p>
                            <div style="margin:16px 0; padding:14px 16px; background-color:#f1f5f9; border:1px solid #e2e8f0; border-radius:12px;">
                                <div style="font-size:13px; line-height:1.7; color:#0f172a;">
                                    <strong style="color:#0f172a;">What to expect</strong><br>
                                    I typically respond within <strong>1–2 business days</strong>. If your request is time-sensitive, feel free to reply to this email with "URGENT" in the subject.
                                </div>
                            </div>
                            <p style="margin:0 0 14px; font-size:14px; line-height:1.7; color:#334155;">While you’re here, you can find more of my work:</p>
                            <p style="margin:0 0 18px;">
                                <a href="https://kaitaing.netlify.app" style="display:inline-block; background-color:#2563eb; color:#ffffff; text-decoration:none; padding:10px 14px; border-radius:10px; font-weight:700; font-size:13px; margin-right:10px;">Portfolio</a>
                                <a href="https://github.com/Kheang1409" style="display:inline-block; background-color:#0f172a; color:#ffffff; text-decoration:none; padding:10px 14px; border-radius:10px; font-weight:700; font-size:13px;">GitHub</a>
                            </p>
                            <p style="margin:0; font-size:14px; line-height:1.7; color:#334155;">Best regards,<br><strong style="color:#0f172a;">Hang Kheang Taing</strong></p>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:16px 28px; background-color:#f8fafc; border-top:1px solid #e2e8f0;">
                            <div style="font-size:12px; color:#64748b; line-height:1.6;">This is an automated response confirming receipt. Please don’t share passwords or sensitive information via email.</div>
                            <div style="margin-top:8px; font-size:12px; color:#94a3b8;">© {utcNow.Year} Hang Kheang Taing</div>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
        }

        private static string BuildAutoReplyText(string name, DateTime utcNow)
        {
                var sb = new StringBuilder();
                sb.AppendLine("Thanks — I received your message");
                sb.AppendLine($"Auto-reply sent: {utcNow:yyyy-MM-dd HH:mm} UTC");
                sb.AppendLine();
                sb.AppendLine($"Hi {name},");
                sb.AppendLine();
                sb.AppendLine("Thank you for reaching out through my portfolio. I’ve received your message and will review it shortly.");
                sb.AppendLine("I typically respond within 1–2 business days.");
                sb.AppendLine();
                sb.AppendLine("Portfolio: https://kaitaing.netlify.app");
                sb.AppendLine("GitHub: https://github.com/Kheang1409");
                sb.AppendLine();
                sb.AppendLine("Best regards,");
                sb.AppendLine("Hang Kheang Taing");
                return sb.ToString();
        }

        private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}