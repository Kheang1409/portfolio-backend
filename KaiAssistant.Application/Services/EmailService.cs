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
        message.Subject = $"🔔 New Contact Form Submission from {Name}";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>Contact Form Submission</title>
                </head>
                <body style="margin: 0; padding: 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f4f4;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f4f4; padding: 40px 20px;">
                        <tr>
                            <td align="center">
                                <table width="600" cellpadding="0" cellspacing="0" style="background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); overflow: hidden;">
                                    <!-- Header -->
                                    <tr>
                                        <td style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px 40px; text-align: center;">
                                            <h1 style="margin: 0; color: #ffffff; font-size: 28px; font-weight: 600; letter-spacing: -0.5px;">
                                                � New Contact Message
                                            </h1>
                                            <p style="margin: 10px 0 0; color: #e0e7ff; font-size: 14px;">
                                                You've received a new inquiry from your website
                                            </p>
                                        </td>
                                    </tr>
                                    
                                    <!-- Content -->
                                    <tr>
                                        <td style="padding: 40px;">
                                            <!-- Sender Info -->
                                            <div style="background-color: #f8f9fa; border-left: 4px solid #667eea; padding: 20px; border-radius: 8px; margin-bottom: 25px;">
                                                <table width="100%" cellpadding="0" cellspacing="0">
                                                    <tr>
                                                        <td style="padding-bottom: 12px;">
                                                            <span style="display: inline-block; color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;">From</span>
                                                            <h3 style="margin: 5px 0 0; color: #1f2937; font-size: 20px; font-weight: 600;">
                                                                {Name}
                                                            </h3>
                                                        </td>
                                                    </tr>
                                                    <tr>
                                                        <td>
                                                            <span style="display: inline-block; color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;">Email</span>
                                                            <p style="margin: 5px 0 0; color: #4b5563; font-size: 16px;">
                                                                <a href="mailto:{Email}" style="color: #667eea; text-decoration: none; font-weight: 500;">
                                                                    {Email}
                                                                </a>
                                                            </p>
                                                        </td>
                                                    </tr>
                                                </table>
                                            </div>
                                            
                                            <!-- Message -->
                                            <div style="margin-bottom: 25px;">
                                                <span style="display: inline-block; color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 10px;">Message</span>
                                                <div style="background-color: #f9fafb; border: 1px solid #e5e7eb; padding: 20px; border-radius: 8px; margin-top: 10px;">
                                                    <p style="margin: 0; color: #374151; font-size: 15px; line-height: 1.7; white-space: pre-wrap; word-wrap: break-word;">
                                                        {Message}
                                                    </p>
                                                </div>
                                            </div>
                                            
                                            <!-- Action Button -->
                                            <div style="text-align: center; margin: 30px 0;">
                                                <a href="mailto:{Email}?subject=Re: Contact Form Submission" 
                                                   style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: #ffffff; text-decoration: none; padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px rgba(102, 126, 234, 0.3); transition: all 0.3s ease;">
                                                    📧 Reply to {Name}
                                                </a>
                                            </div>
                                        </td>
                                    </tr>
                                    
                                    <!-- Footer -->
                                    <tr>
                                        <td style="background-color: #f9fafb; padding: 25px 40px; border-top: 1px solid #e5e7eb;">
                                            <table width="100%" cellpadding="0" cellspacing="0">
                                                <tr>
                                                    <td style="text-align: center;">
                                                        <p style="margin: 0; color: #9ca3af; font-size: 13px; line-height: 1.6;">
                                                            📅 Received on <strong style="color: #6b7280;">{DateTime.UtcNow:dddd, MMMM d, yyyy}</strong> at <strong style="color: #6b7280;">{DateTime.UtcNow:h:mm tt}</strong> UTC
                                                        </p>
                                                        <p style="margin: 10px 0 0; color: #9ca3af; font-size: 12px;">
                                                            Sent via KaiAssistant Contact Form
                                                        </p>
                                                    </td>
                                                </tr>
                                            </table>
                                        </td>
                                    </tr>
                                </table>
                                
                                <!-- Footer Note -->
                                <table width="600" cellpadding="0" cellspacing="0" style="margin-top: 20px;">
                                    <tr>
                                        <td style="text-align: center; padding: 0 20px;">
                                            <p style="margin: 0; color: #9ca3af; font-size: 12px; line-height: 1.5;">
                                                This is an automated message from your contact form.<br>
                                                Please do not reply directly to this email.
                                            </p>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
            """
        };

        await SendEmailAsync(message);
    }

    public async Task SendConfirmationEmailAsync(string Name, string Email)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_emailSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(Email));
        message.Subject = "✅ Thank You for Contacting Me!";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>Message Received</title>
                </head>
                <body style="margin: 0; padding: 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f4f4;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f4f4; padding: 40px 20px;">
                        <tr>
                            <td align="center">
                                <table width="600" cellpadding="0" cellspacing="0" style="background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); overflow: hidden;">
                                    <!-- Header -->
                                    <tr>
                                        <td style="background: linear-gradient(135deg, #10b981 0%, #059669 100%); padding: 40px 40px; text-align: center;">
                                            <div style="background-color: rgba(255, 255, 255, 0.2); width: 80px; height: 80px; margin: 0 auto 20px; border-radius: 50%; display: flex; align-items: center; justify-content: center;">
                                                <span style="font-size: 48px;">✅</span>
                                            </div>
                                            <h1 style="margin: 0; color: #ffffff; font-size: 28px; font-weight: 600; letter-spacing: -0.5px;">
                                                Message Received!
                                            </h1>
                                            <p style="margin: 10px 0 0; color: #d1fae5; font-size: 14px;">
                                                Thank you for reaching out to me
                                            </p>
                                        </td>
                                    </tr>
                                    
                                    <!-- Content -->
                                    <tr>
                                        <td style="padding: 40px;">
                                            <p style="margin: 0 0 20px; color: #1f2937; font-size: 16px; line-height: 1.6;">
                                                Hi <strong style="color: #10b981;">{Name}</strong>,
                                            </p>
                                            
                                            <p style="margin: 0 0 20px; color: #4b5563; font-size: 15px; line-height: 1.7;">
                                                Thank you for contacting me through my portfolio website! I've successfully received your message and I'm excited to connect with you.
                                            </p>
                                            
                                            <div style="background: linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%); border-left: 4px solid #10b981; padding: 20px; border-radius: 8px; margin: 25px 0;">
                                                <p style="margin: 0; color: #166534; font-size: 14px; line-height: 1.6;">
                                                    <strong>📬 What happens next?</strong><br>
                                                    I'll review your message and get back to you as soon as possible, typically within 24-48 hours.
                                                </p>
                                            </div>
                                            
                                            <p style="margin: 25px 0 20px; color: #4b5563; font-size: 15px; line-height: 1.7;">
                                                In the meantime, feel free to explore more of my work:
                                            </p>
                                            
                                            <!-- Social Links -->
                                            <table width="100%" cellpadding="0" cellspacing="0" style="margin: 25px 0;">
                                                <tr>
                                                    <td align="center">
                                                        <table cellpadding="0" cellspacing="0">
                                                            <tr>
                                                                <td style="padding: 0 10px;">
                                                                    <a href="https://kaitaing.netlify.app" style="display: inline-block; background-color: #f3f4f6; color: #374151; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: 500; font-size: 14px; transition: all 0.3s ease;">
                                                                        🌐 Portfolio
                                                                    </a>
                                                                </td>
                                                                <td style="padding: 0 10px;">
                                                                    <a href="https://github.com/Kheang1409" style="display: inline-block; background-color: #f3f4f6; color: #374151; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: 500; font-size: 14px; transition: all 0.3s ease;">
                                                                        💻 GitHub
                                                                    </a>
                                                                </td>
                                                            </tr>
                                                        </table>
                                                    </td>
                                                </tr>
                                            </table>
                                            
                                            <p style="margin: 30px 0 0; color: #4b5563; font-size: 15px; line-height: 1.7;">
                                                Looking forward to connecting with you!
                                            </p>
                                            
                                            <p style="margin: 20px 0 0; color: #1f2937; font-size: 15px; font-weight: 600;">
                                                Best regards,<br>
                                                <span style="color: #10b981; font-size: 18px;">Kai Taing</span>
                                            </p>
                                        </td>
                                    </tr>
                                    
                                    <!-- Footer -->
                                    <tr>
                                        <td style="background-color: #f9fafb; padding: 25px 40px; border-top: 1px solid #e5e7eb;">
                                            <table width="100%" cellpadding="0" cellspacing="0">
                                                <tr>
                                                    <td style="text-align: center;">
                                                        <p style="margin: 0; color: #9ca3af; font-size: 12px; line-height: 1.6;">
                                                            This is an automated confirmation email.<br>
                                                            Please do not reply directly to this message.
                                                        </p>
                                                        <p style="margin: 10px 0 0; color: #9ca3af; font-size: 12px;">
                                                            © {DateTime.UtcNow.Year} Kai Taing. All rights reserved.
                                                        </p>
                                                    </td>
                                                </tr>
                                            </table>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
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