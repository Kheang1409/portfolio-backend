namespace KaiAssistant.Domain.Entities;

public class EmailSettings
{
    public string SmtpServer { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public string SenderEmail { get; private set; } = string.Empty;
    public string RecieverEmail { get; private set; } = string.Empty;
    public string SenderPassword { get; private set; } = string.Empty;

    public EmailSettings(
        string smtpServer,
        int port,
        string senderEmail,
        string recieverEmail,
        string senderPassword)
    {
        SmtpServer = smtpServer;
        Port = port;
        SenderEmail = senderEmail;
        RecieverEmail = recieverEmail;
        SenderPassword = senderPassword;
    }

}