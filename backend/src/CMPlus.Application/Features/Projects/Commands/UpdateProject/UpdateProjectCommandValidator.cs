using FluentValidation;

namespace CMPlus.Application.Features.Projects.Commands.UpdateProject;

/// <summary>
/// Client-visible, server-side rejection layer (S4-BE-02 DoD). Every percent-typed field additionally
/// carries a domain-level defense-in-depth invariant on <c>Project</c> itself
/// (<c>PercentageGuard.Clamp</c>, the same established pattern as every other percent field in this
/// codebase - see <c>Project.cs</c>'s remarks) - this validator is the layer that turns an
/// out-of-range value into an explicit rejection with a message, rather than a silently-clamped save.
/// </summary>
public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Owner).NotEmpty();

        // US-4.3 DoD: "no silent acceptance of an invalid date range."
        RuleFor(x => x.ContractFinish)
            .GreaterThanOrEqualTo(x => x.ContractStart)
            .WithMessage("ContractFinish cannot be earlier than ContractStart.");

        RuleFor(x => x.Bac).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.ContractValue).GreaterThanOrEqualTo(0m);

        // US-4.4 DoD: "a value outside 0.00-100.00 is rejected client-side and server-side
        // (defense in depth)."
        RuleFor(x => x.RetentionRate).InclusiveBetween(0m, 100m).When(x => x.RetentionRate.HasValue);
        RuleFor(x => x.AdvanceRate).InclusiveBetween(0m, 100m).When(x => x.AdvanceRate.HasValue);
        RuleFor(x => x.RetentionCapPercentage).InclusiveBetween(0m, 100m).When(x => x.RetentionCapPercentage.HasValue);
        RuleFor(x => x.RetentionRelease1Percentage).InclusiveBetween(0m, 100m);
        RuleFor(x => x.AdvanceRecoveryStartPct).InclusiveBetween(0m, 100m).When(x => x.AdvanceRecoveryStartPct.HasValue);
        RuleFor(x => x.AdvanceRecoveryRatePct).InclusiveBetween(0m, 100m).When(x => x.AdvanceRecoveryRatePct.HasValue);
        RuleFor(x => x.AdvanceRecoveryEndPct).InclusiveBetween(0m, 100m).When(x => x.AdvanceRecoveryEndPct.HasValue);

        RuleFor(x => x.DefectsLiabilityMonths).GreaterThanOrEqualTo(0).When(x => x.DefectsLiabilityMonths.HasValue);
        RuleFor(x => x.AdvanceAmountPaid).GreaterThanOrEqualTo(0m).When(x => x.AdvanceAmountPaid.HasValue);
        RuleFor(x => x.AdvanceRecoveryMethod).IsInEnum();
    }
}
