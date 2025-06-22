using KaiAssistant.Application.Services;
using MediatR;

namespace KaiAssistant.Application.Contacts.Commands;

public record ContactCommandHandler : IRequestHandler<ContactCommand, bool>
{
    private readonly IEmailService _service;

    public ContactCommandHandler(IEmailService service)
    {
        _service = service;
    }

    public async Task<bool> Handle(ContactCommand command, CancellationToken cancellationToken)
    {
        await _service.SendContactEmailAsync(command.Name, command.Email, command.Message);
        return true;
    }
}