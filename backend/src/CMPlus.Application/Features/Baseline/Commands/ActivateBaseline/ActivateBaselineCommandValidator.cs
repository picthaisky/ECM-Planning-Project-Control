using FluentValidation;

namespace CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;

public sealed class ActivateBaselineCommandValidator : AbstractValidator<ActivateBaselineCommand>
{
    public ActivateBaselineCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.BaselineId).NotEmpty();
    }
}
