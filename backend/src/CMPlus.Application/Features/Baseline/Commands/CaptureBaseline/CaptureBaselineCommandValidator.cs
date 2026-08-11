using FluentValidation;

namespace CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;

public sealed class CaptureBaselineCommandValidator : AbstractValidator<CaptureBaselineCommand>
{
    public CaptureBaselineCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        // nvarchar(250) short-text convention (docs/db-conventions.md §3) - same cap as
        // Project.Name/Code/Owner.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(250);
    }
}
