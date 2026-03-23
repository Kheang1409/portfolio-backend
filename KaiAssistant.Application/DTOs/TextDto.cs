using KaiAssistant.Domain.Entities;

namespace KaiAssistant.Application.DTOs;

public record TextDto(
	string Message,
	ConversationMessage[]? History = null,
	AssistantContextDto? Context = null);
