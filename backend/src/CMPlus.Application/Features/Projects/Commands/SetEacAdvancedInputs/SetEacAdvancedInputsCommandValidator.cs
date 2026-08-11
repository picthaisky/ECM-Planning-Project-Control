using FluentValidation;

namespace CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;

/// <summary>Client-visible mirror of <c>Project.SetEacManualEtc</c>/<c>SetEacCustomPerformanceFactor</c>'s
/// own guards (evm-formulas.md's edge-case table: "$PF_c \le 0$ for CustomPf -> Reject at
/// validation... do not compute"). The Domain setters carry the identical checks as defense in
/// depth - this layer exists so a malformed request never reaches the aggregate at all and gets a
/// field-level <c>validation-error</c> ProblemDetails instead of the generic
/// <c>domain-rule-violation</c> one.</summary>
public sealed class SetEacAdvancedInputsCommandValidator : AbstractValidator<SetEacAdvancedInputsCommand>
{
    public SetEacAdvancedInputsCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        RuleFor(x => x.EacManualEtc)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.EacManualEtc is not null)
            .WithMessage("EacManualEtc cannot be negative.");

        RuleFor(x => x.EacCustomPerformanceFactor)
            .GreaterThan(0m)
            .When(x => x.EacCustomPerformanceFactor is not null)
            .WithMessage("EacCustomPerformanceFactor must be greater than zero when supplied.");
    }
}
