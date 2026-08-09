using FluentValidation;

namespace CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;

/// <summary>Client-visible mirror of the Domain invariants <c>ApprovalPolicy</c>/<c>ApprovalPolicyRule</c>
/// enforce anyway (defense-in-depth, same discipline as every other validator in this codebase) -
/// the contiguous-StepNo-sequence/overlap checks are deliberately NOT duplicated here (they need
/// the whole rule set considered together, not one field at a time); see
/// <see cref="UpdateApprovalPolicyCommandHandler"/> for those.</summary>
public sealed class UpdateApprovalPolicyCommandValidator : AbstractValidator<UpdateApprovalPolicyCommand>
{
    /// <summary>Practical ceiling on approvers-per-step — see the QuorumCount rule below for why an
    /// unbounded value became dangerous once H-02's enforcement landed (S9-SEC-02 finding N-02).</summary>
    public const int MaxQuorumCount = 5;

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
            // Upper bound closes S9-SEC-02 finding N-02. Before H-02 was fixed an absurd
            // QuorumCount was inert because nothing read it; now that the engine genuinely enforces
            // quorum, an over-large value is *binding* — and `DuplicateChainApprover` caps the
            // achievable quorum at the number of distinct users holding that role, so e.g. 3 on a
            // role held by two people can never be satisfied. The certificate then sits in
            // PendingApproval permanently with its money fields frozen, and there is currently no
            // withdraw/cancel endpoint to recover it, so a careless (or malicious) tenant Admin can
            // irrecoverably freeze a contractor's payment claims. 5 is a deliberate practical
            // ceiling: real delegation-of-authority tables top out at two or three signatures per
            // step, so this rejects only values that are already mistakes.
            rule.RuleFor(r => r.QuorumCount)
                .InclusiveBetween(1, MaxQuorumCount)
                .WithMessage(
                    $"QuorumCount must be between 1 and {MaxQuorumCount}. A quorum larger than the number of " +
                    "users holding the required role can never be satisfied, which would strand the document.");
            rule.RuleFor(r => r.MaxAmount)
                .GreaterThan(r => r.MinAmount)
                .When(r => r.MaxAmount.HasValue)
                .WithMessage("MaxAmount must be greater than MinAmount when supplied.");
            rule.RuleFor(r => r.RequiredRole).IsInEnum();
        });
    }
}
