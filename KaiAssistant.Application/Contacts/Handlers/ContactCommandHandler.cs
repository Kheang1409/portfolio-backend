using KaiAssistant.Application.Contacts.Commands;
using KaiAssistant.Application.Events;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
namespace KaiAssistant.Application.Contacts.Handlers;
public record ContactCommandHandler : IRequestHandler<ContactCommand>
{
    private readonly IEmailService _service;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IFeatureFlagService _featureFlags;
    private readonly ILogger<ContactCommandHandler> _logger;
    public ContactCommandHandler(
        IEmailService service,
        IOutboxRepository outboxRepository,
        IFeatureFlagService featureFlags,
        ILogger<ContactCommandHandler> logger)
    {
        _service = service;
        _outboxRepository = outboxRepository;
        _featureFlags = featureFlags;
        _logger = logger;
    }
    public async Task Handle(ContactCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing contact message from {Email} with name {Name}.", command.Email, command.Name);
        try
        {
            // Send notification to portfolio owner
            await _service.SendContactEmailAsync(command.Name, command.Email, command.Message, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Contact email sent successfully for {Email}.", command.Email);
            // Send confirmation email to sender
            await _service.SendConfirmationEmailAsync(command.Name, command.Email, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Confirmation email sent successfully to {Email}.", command.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send contact or confirmation email for {Email}.", command.Email);
            throw;
        }
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{command.Name}|{command.Email}|{command.Message}"));
        var messageHash = Convert.ToHexString(hashBytes);
        var integrationEvent = new ContactMessageSubmittedIntegrationEvent(command.Name, command.Email, messageHash);
        var outboxMessage = OutboxMessageFactory.Create(integrationEvent);
        if (!_featureFlags.EnableOutboxProcessing)
        {
            _logger.LogDebug("Outbox processing is disabled; skipping contact message persistence.");
            return;
        }
        try
        {
            await _outboxRepository.AddAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Outbox persistence timed out for contact message from {Email}; continuing without surfacing the failure.", command.Email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outbox persistence failed for contact message from {Email}; continuing without surfacing the failure.", command.Email);
        }
    }
}