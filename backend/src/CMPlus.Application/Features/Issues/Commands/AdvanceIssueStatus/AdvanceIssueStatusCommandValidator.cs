using FluentValidation;

namespace CMPlus.Application.Features.Issues.Commands.AdvanceIssueStatus;

public sealed class AdvanceIssueStatusCommandValidator : AbstractValidator<AdvanceIssueStatusCommand>
{
    public AdvanceIssueStatusCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.IssueId).NotEmpty();
    }
}
