using MediatR;

namespace KaiAssistant.Application.AskAssistants.Commands;

public record AskAssistantCommand(string Question) : IRequest<string>;
