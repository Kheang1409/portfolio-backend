using System.Net;
using System.Net.Mail;
using System.Text;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
namespace KaiAssistant.Infrastructure.Services;
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly IResilienceStatusProvider _resilience;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly AsyncPolicy _sendPolicy;
    public SmtpEmailService(
        EmailSettings emailSettings,
        IResilienceStatusProvider resilience,
        ILogger<SmtpEmailService> logger)
    {
        _emailSettings = emailSettings;
        _resilience = resilience;
        _logger = logger;
        var retry = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromMilliseconds(Math.Min(2500, 200 * Math.Pow(2, attempt))) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 120)));
        var timeout = Policy.TimeoutAsync(TimeSpan.FromSeconds(30));
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
    public async Task SendContactEmailAsync(string name, string email, string message, CancellationToken cancellationToken = default)
    {
        if (!_emailSettings.Enabled)
        {
            _logger.LogInformation("Email delivery is disabled; skipping portfolio contact notification for {Email}.", email);
            return;
        }
        var utcNow = DateTime.UtcNow;
        var subject = $"New portfolio contact: {name}";
        var htmlBody = BuildOwnerNotificationHtml(name, email, message, utcNow);
        var textBody = BuildOwnerNotificationText(name, email, message, utcNow);
        await SendEmailAsync(
            recipient: _emailSettings.RecieverEmail,
            subject: subject,
            htmlBody: htmlBody,
            textBody: textBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    public async Task SendConfirmationEmailAsync(string name, string email, CancellationToken cancellationToken = default)
    {
        if (!_emailSettings.Enabled)
        {
            _logger.LogInformation("Email delivery is disabled; skipping portfolio confirmation email for {Email}.", email);
            return;
        }
        var utcNow = DateTime.UtcNow;
        var subject = "Thanks for reaching out - your message is in";
        var htmlBody = BuildAutoReplyHtml(name, utcNow);
        var textBody = BuildAutoReplyText(name, utcNow);
        await SendEmailAsync(
            recipient: email,
            subject: subject,
            htmlBody: htmlBody,
            textBody: textBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    private async Task SendEmailAsync(
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("Skipping email send for subject {Subject} because the recipient address is empty.", subject);
            return;
        }
        try
        {
            _logger.LogInformation("Starting email send for {Recipient} with subject {Subject}. Timeout: {TimeoutMs}ms", recipient, subject, 10000);
            await _sendPolicy.ExecuteAsync(async ct =>
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(_emailSettings.SenderEmail),
                    Subject = subject,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                mail.To.Add(recipient);
                using var smtp = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.Port)
                {
                    Credentials = new NetworkCredential(_emailSettings.SenderEmail, _emailSettings.SenderPassword),
                    EnableSsl = _emailSettings.Port == 465 || _emailSettings.Port == 587,
                    UseDefaultCredentials = false,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };
                _logger.LogInformation("SMTP client configured - attempting to send email to {Recipient} via {SmtpServer}:{Port} with SSL={UseSSL}.", recipient, _emailSettings.SmtpServer, _emailSettings.Port, _emailSettings.Port == 465 || _emailSettings.Port == 587);
#if NET10_0_OR_GREATER
                await smtp.SendMailAsync(mail, ct).ConfigureAwait(false);
#else
                ct.ThrowIfCancellationRequested();
                await smtp.SendMailAsync(mail).ConfigureAwait(false);
#endif
                _logger.LogInformation("Email successfully sent to {Recipient} with subject {Subject}.", recipient, subject);
                _resilience.RecordSuccess("email");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _resilience.RecordFailure("email", ex.Message);
            _logger.LogError(ex, "Email send failed for subject {Subject}. Exception type: {ExceptionType}, Message: {Message}. Continuing without surfacing the failure to the contact form.", subject, ex.GetType().Name, ex.Message);
            // Log inner exceptions if available
            if (ex.InnerException != null)
            {
                _logger.LogError(ex.InnerException, "Inner exception details: {InnerExceptionType}: {InnerMessage}", ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
        }
    }
    private static string BuildOwnerNotificationHtml(string name, string email, string messageText, DateTime utcNow)
    {
        var safeName = HtmlEncode(name);
        var safeEmail = HtmlEncode(email);
        var safeMessage = HtmlEncode(messageText);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>New Portfolio Message</title>
    <style>
        @media only screen and (max-width: 640px) {
            .shell { padding: 16px !important; }
            .card { border-radius: 14px !important; }
            .content { padding: 20px !important; }
            .hero { padding: 22px 20px !important; }
            .title { font-size: 22px !important; }
        }
    </style>
</head>
<body style="margin:0; padding:0; background-color:#eef2ff; font-family: 'Segoe UI', Tahoma, Arial, sans-serif; color:#0f172a;">
    <table width="100%" cellpadding="0" cellspacing="0" role="presentation" class="shell" style="padding:32px 16px; background:linear-gradient(180deg,#eef2ff 0%,#f8fafc 100%);">
        <tr>
            <td align="center">
                <table width="680" cellpadding="0" cellspacing="0" role="presentation" class="card" style="max-width:680px; width:100%; background-color:#ffffff; border:1px solid #dbe4ff; border-radius:20px; overflow:hidden; box-shadow:0 18px 48px rgba(15,23,42,.10);">
                    <tr>
                        <td class="hero" style="padding:28px 30px; background:linear-gradient(135deg,#0f172a 0%,#1d4ed8 100%);">
                            <div style="font-size:12px; letter-spacing:.14em; text-transform:uppercase; color:#cbd5e1;">KaiAssistant - Portfolio Contact</div>
                            <div class="title" style="margin-top:10px; font-size:26px; line-height:1.2; font-weight:800; color:#ffffff;">You received a new message</div>
                            <div style="margin-top:10px; font-size:13px; color:#dbeafe;">Received {{utcNow:yyyy-MM-dd HH:mm}} UTC</div>
                        </td>
                    </tr>
                    <tr>
                        <td class="content" style="padding:28px 30px;">
                            <p style="margin:0 0 18px; font-size:15px; line-height:1.75; color:#334155;">A visitor reached out through your portfolio contact form. The message summary is below, with a direct reply shortcut for faster follow-up.</p>
                            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:0 0 20px;">
                                <tr>
                                    <td style="padding:16px 18px; background:linear-gradient(180deg,#f8fafc 0%,#eef2ff 100%); border:1px solid #dbe4ff; border-radius:14px;">
                                        <div style="font-size:12px; color:#475569; text-transform:uppercase; letter-spacing:.08em;">From</div>
                                        <div style="margin-top:6px; font-size:18px; font-weight:800; color:#0f172a;">{{safeName}}</div>
                                        <div style="margin-top:6px; font-size:14px; color:#334155;"><a href="mailto:{{safeEmail}}" style="color:#2563eb; text-decoration:none;">{{safeEmail}}</a></div>
                                        <div style="margin-top:14px; display:inline-block; padding:6px 10px; border-radius:999px; background-color:#dbeafe; color:#1d4ed8; font-size:12px; font-weight:700;">New inquiry</div>
                                    </td>
                                </tr>
                            </table>
                            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:0 0 20px;">
                                <tr>
                                    <td align="left">
                                        <a href="mailto:{{safeEmail}}" style="display:inline-block; padding:12px 18px; background-color:#1d4ed8; color:#ffffff; text-decoration:none; border-radius:12px; font-size:14px; font-weight:700; box-shadow:0 8px 20px rgba(29,78,216,.22);">Reply to {{safeName}}</a>
                                    </td>
                                </tr>
                            </table>
                            <div style="font-size:12px; color:#475569; text-transform:uppercase; letter-spacing:.08em;">Message</div>
                            <div style="margin-top:10px; padding:18px; background-color:#ffffff; border:1px solid #dbe4ff; border-radius:14px;">
                                <div style="white-space:pre-wrap; word-break:break-word; font-size:14px; line-height:1.8; color:#0f172a;">{{safeMessage}}</div>
                            </div>
                            <p style="margin:18px 0 0; font-size:12px; line-height:1.7; color:#64748b;">This message was sanitized before rendering. Use the reply button above to continue the conversation quickly.</p>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:20px 30px; border-top:1px solid #dbe4ff; background-color:#f8fafc;">
                            <div style="font-size:11px; line-height:1.6; color:#94a3b8;">
                                <strong style="color:#64748b;">Important:</strong> This is a system notification from your portfolio contact form. If you need to reach the visitor directly, please reply using the button above. The sender will receive confirmation of their message.
                            </div>
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
        sb.AppendLine("New contact message from your portfolio");
        sb.AppendLine($"Received: {utcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine($"Name: {name}");
        sb.AppendLine($"Email: {email}");
        sb.AppendLine();
        sb.AppendLine("Message:");
        sb.AppendLine(messageText);
        sb.AppendLine();
        sb.AppendLine("Reply directly by email to continue the conversation.");
        return sb.ToString();
    }
    private static string BuildAutoReplyHtml(string name, DateTime utcNow)
    {
        var safeName = HtmlEncode(name);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Message Received</title>
    <style>
        @media only screen and (max-width: 640px) {
            .shell { padding: 16px !important; }
            .card { border-radius: 14px !important; }
            .content { padding: 20px !important; }
            .hero { padding: 22px 20px !important; }
            .title { font-size: 22px !important; }
        }
    </style>
</head>
<body style="margin:0; padding:0; background-color:#f0f9ff; font-family: 'Segoe UI', Tahoma, Arial, sans-serif; color:#0f172a;">
    <table width="100%" cellpadding="0" cellspacing="0" role="presentation" class="shell" style="padding:32px 16px; background:linear-gradient(180deg,#f0f9ff 0%,#e0f2fe 100%);">
        <tr>
            <td align="center">
                <table width="680" cellpadding="0" cellspacing="0" role="presentation" class="card" style="max-width:680px; width:100%; background-color:#ffffff; border:1px solid #bae6fd; border-radius:20px; overflow:hidden; box-shadow:0 18px 48px rgba(15,23,42,.10);">
                    <tr>
                        <td class="hero" style="padding:28px 30px; background:linear-gradient(135deg,#0369a1 0%,#0284c7 100%);">
                            <div style="font-size:11px; letter-spacing:.2em; text-transform:uppercase; color:#bae6fd;">Portfolio Contact Form</div>
                            <div class="title" style="margin-top:12px; font-size:28px; line-height:1.2; font-weight:800; color:#ffffff;">We got your message!</div>
                            <div style="margin-top:8px; font-size:13px; color:#cffafe;">Confirmation received {{utcNow:yyyy-MM-dd HH:mm}} UTC</div>
                        </td>
                    </tr>
                    <tr>
                        <td class="content" style="padding:32px 30px;">
                            <p style="margin:0 0 20px; font-size:15px; line-height:1.75; color:#0f172a;">Hi <strong>{{safeName}}</strong>,</p>
                            <div style="padding:14px 16px; background-color:#ecfdf5; border-left:4px solid #10b981; border-radius:8px; margin:0 0 20px;">
                                <div style="font-size:13px; line-height:1.6; color:#065f46;">
                                    <strong>✓ Message received!</strong> Your contact form submission has been successfully delivered. Thank you for reaching out.
                                </div>
                            </div>
                            <p style="margin:0 0 16px; font-size:14px; line-height:1.75; color:#334155;">I appreciate you taking the time to message me through my portfolio. Your message has been received and I'll review it carefully.</p>
                            <p style="margin:0 0 20px; font-size:14px; line-height:1.75; color:#334155;"><strong>Response timeline:</strong> I typically respond within 1-2 business days. If your inquiry is time-sensitive, please mark it as urgent in your message.</p>
                            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:0 0 24px;">
                                <tr>
                                    <td align="left">
                                        <a href="https://kaitaing.netlify.app" style="display:inline-block; padding:12px 20px; background-color:#0284c7; color:#ffffff; text-decoration:none; border-radius:12px; font-size:14px; font-weight:700; box-shadow:0 8px 20px rgba(2,132,199,.22);">View My Portfolio</a>
                                    </td>
                                </tr>
                            </table>
                            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:0;">
                                <tr>
                                    <td style="padding:14px 16px; border:1px solid #bae6fd; border-radius:12px; background:linear-gradient(180deg,#f0f9ff 0%,#e0f2fe 100%); font-size:13px; color:#0c4a6e;">
                                        Connect with me on <a href="https://github.com/Kheang1409" style="color:#0284c7; text-decoration:none; font-weight:700;">GitHub</a> to see my latest projects.
                                    </td>
                                </tr>
                            </table>
                            <p style="margin:20px 0 0; font-size:14px; line-height:1.75; color:#334155;">Best regards,<br><strong style="color:#0f172a;">Hang Kheang Taing</strong></p>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:18px 30px; border-top:1px solid #bae6fd; background-color:#f8fafc;">
                            <div style="font-size:11px; line-height:1.6; color:#64748b;">
                                <div style="margin-bottom:8px;">
                                    <strong style="color:#0f172a;">⚠️ Important:</strong> This is an auto-generated confirmation email.
                                </div>
                                <div style="margin-bottom:8px;">
                                    <strong>Please do not reply to this email.</strong> This address is a no-reply account and <strong>no one monitors</strong> messages sent here.
                                </div>
                                <div>
                                    Your message has been received by the portfolio owner and will be reviewed. You will receive a direct response if needed.
                                </div>
                            </div>
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
        sb.AppendLine("MESSAGE RECEIVED - AUTO-REPLY");
        sb.AppendLine($"Received: {utcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine($"Hi {name},");
        sb.AppendLine();
        sb.AppendLine("Your contact form submission has been successfully received. Thank you for reaching out!");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: This is an auto-generated confirmation email.");
        sb.AppendLine("- Please do not reply to this email");
        sb.AppendLine("- This is a no-reply address and NO ONE monitors messages sent here");
        sb.AppendLine("- Your message has been delivered to the portfolio owner");
        sb.AppendLine("- You will receive a direct response if needed");
        sb.AppendLine();
        sb.AppendLine("Response timeline: Usually within 1-2 business days");
        sb.AppendLine();
        sb.AppendLine("Connect with me:");
        sb.AppendLine("Portfolio: https://kaitaing.netlify.app");
        sb.AppendLine("GitHub: https://github.com/Kheang1409");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("Hang Kheang Taing");
        return sb.ToString();
    }
    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}