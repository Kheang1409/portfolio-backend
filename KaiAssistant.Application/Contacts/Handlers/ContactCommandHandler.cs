using KaiAssistant.Application.Contacts.Commands;
using KaiAssistant.Application.Events;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Services;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace KaiAssistant.Application.Contacts.Handlers;

public record ContactCommandHandler : IRequestHandler<ContactCommand>
{
    private readonly IEmailService _service;
    private readonly IOutboxRepository _outboxRepository;

    public ContactCommandHandler(IEmailService service, IOutboxRepository outboxRepository)
    {
        _service = service;
        _outboxRepository = outboxRepository;
    }

    public async Task Handle(ContactCommand command, CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _service.SendContactEmailAsync(command.Name, command.Email, command.Message, cancellationToken),
            _service.SendConfirmationEmailAsync(command.Name, command.Email, cancellationToken)
        );

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{command.Name}|{command.Email}|{command.Message}"));
        var messageHash = Convert.ToHexString(hashBytes);

        var integrationEvent = new ContactMessageSubmittedIntegrationEvent(command.Name, command.Email, messageHash);
        var outboxMessage = OutboxMessageFactory.Create(integrationEvent);

        await _outboxRepository.AddAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
    }
}
