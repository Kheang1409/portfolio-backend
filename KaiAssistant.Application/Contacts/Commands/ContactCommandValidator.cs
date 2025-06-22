using FluentValidation;

namespace KaiAssistant.Application.Contacts.Commands;

public class ContactCommandValidator : AbstractValidator<ContactCommand>
{
    public ContactCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be valid.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.");
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MinimumLength(2).WithMessage("Message at least 2 characters!");
    }
}
