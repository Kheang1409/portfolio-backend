using KaiAssistant.Application.Interfaces;
using MediatR;

namespace KaiAssistant.Application.AskAssistants.Commands;

public class AskAssistantCommandHandler : IRequestHandler<AskAssistantCommand, string>
{
    private readonly IAssistantService _service;
    public AskAssistantCommandHandler(IAssistantService service)
    {
        _service = service;
    }

    public async Task<string> Handle(AskAssistantCommand command, CancellationToken cancellationToken)
    {
        var response = await _service.AskQuestionAsync(command.Question, command.UserId);
        return response;
    }
}