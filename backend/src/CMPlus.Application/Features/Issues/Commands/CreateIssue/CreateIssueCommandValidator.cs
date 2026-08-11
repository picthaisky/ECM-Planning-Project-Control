using FluentValidation;

namespace CMPlus.Application.Features.Issues.Commands.CreateIssue;

public sealed class CreateIssueCommandValidator : AbstractValidator<CreateIssueCommand>
{
    public CreateIssueCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Detail).MaximumLength(2000);
        RuleFor(x => x.Owner).MaximumLength(200);
    }
}
