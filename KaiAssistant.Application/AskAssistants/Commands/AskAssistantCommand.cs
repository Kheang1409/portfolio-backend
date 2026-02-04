using KaiAssistant.Domain.Entities;
using MediatR;

namespace KaiAssistant.Application.AskAssistants.Commands;

public record AskAssistantCommand(string Question, ConversationMessage[]? History = null) : IRequest<string>;
