using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Diagnostics;
using MediatR;

namespace KaiAssistant.Application.AskAssistants.Commands;

public record AskAssistantCommand(
	string Question,
	ConversationMessage[]? History = null,
	AssistantContext? Context = null) : IRequest<AiResponse>;
