namespace KaiAssistant.Infrastructure.EventBus;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; set; } = false;
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "kaiassistant.events";
    public string RoutingKeyPrefix { get; set; } = "kaiassistant";
}
