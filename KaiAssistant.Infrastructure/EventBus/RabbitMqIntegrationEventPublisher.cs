using System.Text;
using System.Text.Json;
using System.Diagnostics;
using KaiAssistant.Application.Events;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Infrastructure.FeatureFlags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using RabbitMQ.Client;
namespace KaiAssistant.Infrastructure.EventBus;
public sealed class RabbitMqIntegrationEventPublisher : IIntegrationEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly IFeatureFlagService _featureFlags;
    private readonly IResilienceStatusProvider _resilience;
    private readonly ILogger<RabbitMqIntegrationEventPublisher> _logger;
    private readonly AsyncPolicy _publishPolicy;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;
    public RabbitMqIntegrationEventPublisher(
        IOptions<RabbitMqOptions> options,
        IFeatureFlagService featureFlags,
        IResilienceStatusProvider resilience,
        ILogger<RabbitMqIntegrationEventPublisher> logger)
    {
        _options = options.Value;
        _featureFlags = featureFlags;
        _resilience = resilience;
        _logger = logger;
        var retry = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(Math.Min(2000, 200 * Math.Pow(2, attempt))),
                (exception, timespan, retryCount, _) =>
                {
                    _logger.LogWarning(exception, "RabbitMQ publish retry {RetryCount} after {Delay}.", retryCount, timespan);
                });
        var timeout = Policy.TimeoutAsync(TimeSpan.FromSeconds(5));
        var circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
                (ex, breakDelay) =>
                {
                    _resilience.RecordFailure("rabbitmq", ex.Message);
                    _resilience.RecordCircuitState("rabbitmq", "Open");
                    _logger.LogWarning(ex, "RabbitMQ circuit opened for {BreakDelay}.", breakDelay);
                },
                () =>
                {
                    _resilience.RecordCircuitState("rabbitmq", "Closed");
                    _logger.LogInformation("RabbitMQ circuit reset.");
                },
                () => _resilience.RecordCircuitState("rabbitmq", "HalfOpen"));
        _publishPolicy = Policy.WrapAsync(retry, timeout, circuitBreaker);
    }
    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!_featureFlags.EnableRabbitMqPublishing || !_options.Enabled)
        {
            _logger.LogDebug("RabbitMQ publisher disabled; skipping publish for {EventType}.", integrationEvent.GetType().Name);
            return;
        }
        await _publishPolicy.ExecuteAsync(async ct =>
        {
            EnsureConnection();
            var envelope = new
            {
                integrationEvent.EventId,
                IdempotencyKey = string.IsNullOrWhiteSpace(integrationEvent.IdempotencyKey)
                    ? integrationEvent.EventId.ToString("N")
                    : integrationEvent.IdempotencyKey,
                integrationEvent.OccurredAtUtc,
                EventType = integrationEvent.GetType().FullName,
                Payload = integrationEvent
            };
            var payload = JsonSerializer.Serialize(envelope);
            var body = Encoding.UTF8.GetBytes(payload);
            var routingKey = string.IsNullOrWhiteSpace(_options.RoutingKeyPrefix)
                ? integrationEvent.GetType().Name
                : $"{_options.RoutingKeyPrefix}.{integrationEvent.GetType().Name}";
            lock (_sync)
            {
                var props = _channel!.CreateBasicProperties();
                props.ContentType = "application/json";
                props.DeliveryMode = 2;
                props.MessageId = integrationEvent.EventId.ToString("N");
                props.Timestamp = new AmqpTimestamp(integrationEvent.OccurredAtUtc.ToUnixTimeSeconds());
                props.Headers ??= new Dictionary<string, object>();
                props.Headers["idempotency-key"] = string.IsNullOrWhiteSpace(integrationEvent.IdempotencyKey)
                    ? integrationEvent.EventId.ToString("N")
                    : integrationEvent.IdempotencyKey;
                var currentActivity = Activity.Current;
                if (!string.IsNullOrWhiteSpace(currentActivity?.Id))
                {
                    props.Headers["traceparent"] = currentActivity.Id;
                }
                if (!string.IsNullOrWhiteSpace(currentActivity?.TraceStateString))
                {
                    props.Headers["tracestate"] = currentActivity.TraceStateString;
                }
                _channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    basicProperties: props,
                    body: body);
            }
            _resilience.RecordSuccess("rabbitmq");
            _logger.LogInformation(
                "Event published to RabbitMQ: eventId={EventId} eventType={EventType} routingKey={RoutingKey}",
                integrationEvent.EventId,
                integrationEvent.GetType().Name,
                routingKey);
            await Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
    }
    private void EnsureConnection()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
        {
            return;
        }
        lock (_sync)
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            {
                return;
            }
            _channel?.Dispose();
            _connection?.Dispose();
            if (string.IsNullOrWhiteSpace(_options.HostName))
            {
                _resilience.RecordFailure("rabbitmq", "Missing host configuration.");
                throw new InvalidOperationException("RabbitMq:HostName must be configured when RabbitMq:Enabled is true.");
            }
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        }
    }
    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}