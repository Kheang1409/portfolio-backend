using FluentValidation;

namespace KaiAssistant.Application.AskAssistants.Commands;

public class AskAssistantCommandValidator : AbstractValidator<AskAssistantCommand>
{
    public AskAssistantCommandValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Question is required.")
            .MaximumLength(2000).WithMessage("Question cannot exceed 2000 characters.");

        RuleForEach(x => x.History)
            .ChildRules(history =>
            {
                history.RuleFor(h => h.Role)
                    .NotEmpty()
                    .MaximumLength(32);

                history.RuleFor(h => h.Content)
                    .NotEmpty()
                    .MaximumLength(4000);
            });
    }
}
