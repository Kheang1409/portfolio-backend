using KaiAssistant.Application.Contacts.Commands;
using KaiAssistant.Application.Services;
using MediatR;

namespace KaiAssistant.Application.Contacts.Handlers;

public record ContactCommandHandler : IRequestHandler<ContactCommand>
{
    private readonly IEmailService _service;

    public ContactCommandHandler(IEmailService service)
    {
        _service = service;
    }

    public async Task Handle(ContactCommand command, CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _service.SendContactEmailAsync(command.Name, command.Email, command.Message),
            _service.SendConfirmationEmailAsync(command.Name, command.Email)
        );
    }
}
