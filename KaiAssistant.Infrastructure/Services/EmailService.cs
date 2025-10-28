using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities;
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;

namespace KaiAssistant.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendContactEmailAsync(string Name, string Email, string Message)
    {
                var subject = $"New contact from {Name} — {Email}";

                var html = $@"
                            <html>
                            <body style='font-family:Segoe UI, Roboto, Helvetica, Arial, sans-serif; color:#222;'>
                                <h2 style='color:#0b57d0'>New contact submission</h2>
                                <p><strong>Name:</strong> {System.Net.WebUtility.HtmlEncode(Name)}</p>
                                <p><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(Email)}</p>
                                <p><strong>Message:</strong></p>
                                <div style='margin:8px 0;padding:12px;background:#f6f8fb;border-radius:6px;color:#111;'>{System.Net.WebUtility.HtmlEncode(Message).Replace("\n","<br/>")}</div>
                                <hr/>
                                <p style='font-size:0.9em;color:#666'>Received at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>
                            </body>
                            </html>";

                var text = $"New contact submission\nName: {Name}\nEmail: {Email}\n\nMessage:\n{Message}\n\nReceived at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Kai Assistant", _settings.SenderEmail));
                message.To.Add(MailboxAddress.Parse(_settings.RecieverEmail));
                message.Subject = subject;
                try { message.ReplyTo.Add(MailboxAddress.Parse(Email)); } catch { }

                var builder = new BodyBuilder { HtmlBody = html, TextBody = text };
                message.Body = builder.ToMessageBody();

                await SendMimeMessageAsync(message).ConfigureAwait(false);
    }

    public async Task SendConfirmationEmailAsync(string Name, string Email)
    {
        var subject = "Thanks — we received your message";

        var html = $@"
                    <html>
                        <body style='font-family:Segoe UI, Roboto, Helvetica, Arial, sans-serif; color:#222;'>
                            <h2 style='color:#0b57d0'>Thanks for reaching out, {System.Net.WebUtility.HtmlEncode(Name)}</h2>
                            <p>We've received your message and Kai will get back to you shortly.</p>
                            <hr/>
                            <p style='font-size:0.95em;color:#333'><strong>What we received:</strong></p>
                            <div style='margin:8px 0;padding:12px;background:#f6f8fb;border-radius:6px;color:#111;'>
                                <p><strong>From:</strong> {System.Net.WebUtility.HtmlEncode(Name)} &lt;{System.Net.WebUtility.HtmlEncode(Email)}&gt;</p>
                            </div>
                            <p style='font-size:0.85em;color:#666'>If you didn't expect this message, please ignore it.</p>
                            <p style='font-size:0.85em;color:#666'>— Kai Assistant</p>
                        </body>
                    </html>";

        var text = $"Hi {Name},\n\nThanks — we've received your message and will get back to you shortly.\n\n— Kai Assistant";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Kai Assistant", _settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(Email));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = html, TextBody = text };
        message.Body = builder.ToMessageBody();

        await SendMimeMessageAsync(message).ConfigureAwait(false);
    }

    private async Task SendMimeMessageAsync(MimeMessage message)
    {
        if (!_settings.Enabled)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown";
            _logger.LogInformation("Email sending disabled (Enabled={Enabled}, Environment={Environment}). Saved message to dev-emails instead of sending to {To}.", _settings.Enabled, environment, string.Join(',', message.To.Select(m => m.ToString())));
            TrySaveMessageToDisk(message, "disabled");
            return;
        }

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _settings.Port == 465
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_settings.SmtpServer, _settings.Port, socketOptions).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_settings.SenderEmail) && !string.IsNullOrWhiteSpace(_settings.SenderPassword))
            {
                try
                {
                    await client.AuthenticateAsync(_settings.SenderEmail, _settings.SenderPassword).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var savedPath = TrySaveMessageToDisk(message, "auth-failure");
                    _logger.LogWarning(ex, "SMTP authentication failed for {SenderEmail}. Email not sent to {To}. Saved unsent message to {SavedPath}.", _settings.SenderEmail, string.Join(',', message.To.Select(m => m.ToString())), savedPath ?? "n/a");
                    return;
                }
            }

            await client.SendAsync(message).ConfigureAwait(false);
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
        {
            var savedPath = TrySaveMessageToDisk(message, "protocol-error");
            _logger.LogWarning(ex, "SMTP protocol error while sending email to {To}. Saved unsent message to {SavedPath}.", string.Join(',', message.To.Select(m => m.ToString())), savedPath ?? "n/a");
        }
        catch (Exception ex)
        {
            var savedPath = TrySaveMessageToDisk(message, "exception");
            _logger.LogError(ex, "Unexpected error while sending email to {To}. Saved unsent message to {SavedPath}.", string.Join(',', message.To.Select(m => m.ToString())), savedPath ?? "n/a");
        }
        finally
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting SMTP client.");
            }
        }
    }

    private async Task SendInternalEmailAsync(string to, string subject, string htmlBody)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown";
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Email sending disabled (Enabled={Enabled}, Environment={Environment}). Skipping send to {To}.", _settings.Enabled, environment, to);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _settings.Port == 465
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_settings.SmtpServer, _settings.Port, socketOptions).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_settings.SenderEmail) && !string.IsNullOrWhiteSpace(_settings.SenderPassword))
            {
                try
                {
                    await client.AuthenticateAsync(_settings.SenderEmail, _settings.SenderPassword).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Authentication failed: save the unsent message and log the saved path for developers.
                    var savedPath = TrySaveMessageToDisk(message, "auth-failure");
                    _logger.LogWarning(ex, "SMTP authentication failed for {SenderEmail}. Email not sent to {To}. Saved unsent message to {SavedPath}.", _settings.SenderEmail, to, savedPath ?? "n/a");
                    return;
                }
            }

            await client.SendAsync(message).ConfigureAwait(false);
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
        {
            var savedPath = TrySaveMessageToDisk(message, "protocol-error");
            _logger.LogWarning(ex, "SMTP protocol error while sending email to {To}. Saved unsent message to {SavedPath}.", to, savedPath ?? "n/a");
        }
        catch (Exception ex)
        {
            var savedPath = TrySaveMessageToDisk(message, "exception");
            _logger.LogError(ex, "Unexpected error while sending email to {To}. Saved unsent message to {SavedPath}.", to, savedPath ?? "n/a");
        }
        finally
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting SMTP client.");
            }
        }

        }

    private string? TrySaveMessageToDisk(MimeMessage? message, string reason)
    {
        if (message is null)
        {
            _logger.LogDebug("No MimeMessage available to save to disk (null). Reason: {Reason}", reason);
            return null;
        }

        try
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "dev-emails");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_ffff");
            var safeSubject = string.Join("_", (message.Subject ?? "no-subject").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(' ', '_');
            var fileName = $"email_{timestamp}_{safeSubject}_{reason}.eml";
            var path = Path.Combine(folder, fileName);

            using var stream = File.Create(path);
            message.WriteTo(stream);
            _logger.LogInformation("Saved unsent email to {Path}", path);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save unsent email to disk.");
            return null;
        }
    }
}
