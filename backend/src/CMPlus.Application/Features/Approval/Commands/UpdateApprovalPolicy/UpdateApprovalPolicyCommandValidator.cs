using FluentValidation;

namespace CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;

/// <summary>Client-visible mirror of the Domain invariants <c>ApprovalPolicy</c>/<c>ApprovalPolicyRule</c>
/// enforce anyway (defense-in-depth, same discipline as every other validator in this codebase) -
/// the contiguous-StepNo-sequence/overlap checks are deliberately NOT duplicated here (they need
/// the whole rule set considered together, not one field at a time); see
/// <see cref="UpdateApprovalPolicyCommandHandler"/> for those.</summary>
public sealed class UpdateApprovalPolicyCommandValidator : AbstractValidator<UpdateApprovalPolicyCommand>
{
    public UpdateApprovalPolicyCommandValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();

        RuleFor(x => x.CumulativeVoEscalationPct)
            .InclusiveBetween(0m, 100m)
            .When(x => x.CumulativeVoEscalationPct.HasValue);

        RuleFor(x => x.Rules).NotEmpty().WithMessage("An approval policy must define at least one rule.");

        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.StepNo).GreaterThanOrEqualTo(1);
            rule.RuleFor(r => r.MinAmount).GreaterThanOrEqualTo(0m);
            rule.RuleFor(r => r.QuorumCount).GreaterThanOrEqualTo(1);
            rule.RuleFor(r => r.MaxAmount)
                .GreaterThan(r => r.MinAmount)
                .When(r => r.MaxAmount.HasValue)
                .WithMessage("MaxAmount must be greater than MinAmount when supplied.");
            rule.RuleFor(r => r.RequiredRole).IsInEnum();
        });
    }
}
