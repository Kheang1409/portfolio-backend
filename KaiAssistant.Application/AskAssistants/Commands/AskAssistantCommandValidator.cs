using FluentValidation;

namespace KaiAssistant.Application.AskAssistants.Commands;

public class AskAssistantCommandValidator : AbstractValidator<AskAssistantCommand>
{
    public AskAssistantCommandValidator()
    {

        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Question is required.");
    }
}
