namespace KaiAssistant.Domain.Entities;
public class Message
{
    public string Role { get; set; }
    public string Content { get; set; }

    public Message(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
