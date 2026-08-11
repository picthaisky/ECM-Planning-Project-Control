namespace CMPlus.Application.Features.Approval;

/// <summary>
/// Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the S9-BE-06 approval-policy
/// write API. <see cref="BandGapPrefix"/>/<see cref="BandOverlapPrefix"/> carry the offending
/// <c>StepNo</c> as a dynamic suffix (same idiom as <c>CpmValidationErrorCodes.CycleDetected</c>/
/// <c>EvmErrorCodes.InvalidEacVariantPrefix</c>) so <c>ResultProblemMapper</c> can surface it as a
/// machine-readable <c>invalidStepNo</c> extension on the <c>400</c> response (design.md §2.2:
/// "400 body carries { invalidStepNo, problem }").
/// </summary>
public static class ApprovalPolicyErrorCodes
{
    /// <summary>
    /// Two rules for the <b>same</b> <c>StepNo</c> define intersecting <c>[MinAmount, MaxAmount)</c>
    /// ranges - for an amount inside the overlap, routing would resolve two different rules (and
    /// therefore two different required roles) to the identical step number, which is ambiguous.
    /// Detected in <see cref="Commands.UpdateApprovalPolicy.UpdateApprovalPolicyCommandHandler"/>
    /// itself, as an additive pre-check <i>in front of</i> <c>ApprovalPolicy</c>'s own band
    /// validation: <c>ApprovalPolicy.ValidateBands</c> (Sprint 2) only asserts that the StepNo set
    /// covering any given atomic amount interval is exactly the contiguous sequence
    /// <c>{1,...,k}</c> - a genuine same-StepNo range overlap collapses to one entry via
    /// <c>Distinct()</c> in that check and is not otherwise caught, a pre-existing Sprint 2 gap this
    /// task found and is closing without editing that already-tested method (see the backend-developer
    /// report for S9-BE-05/06).
    /// </summary>
    public const string BandOverlapPrefix = "ApprovalPolicyBandOverlap:";

    /// <summary>
    /// The submitted rule set is not a contiguous <c>StepNo</c> sequence for some amount range (a
    /// step is skipped) - <c>ApprovalPolicy.ValidateBands</c> (Sprint 2, already tested) remains the
    /// single source of truth for this check; this code just carries its already-identified
    /// <c>StepNo</c> back out of the <see cref="CMPlus.Domain.Common.DomainException"/> message as a
    /// dynamic suffix.
    /// </summary>
    public const string BandGapPrefix = "ApprovalPolicyBandGap:";

    /// <summary>Two concurrent <c>PUT .../approval-policies/{documentType}</c> requests both read the
    /// same pre-race active policy before either committed, then each staged a different
    /// <c>nextVersion</c> - the same-shape race <c>BaselineErrorCodes.ConcurrencyConflict</c>'s own
    /// remarks describe for Baseline activation, reachable here via
    /// <c>IApprovalPolicyRepository.SaveChangesAsync</c> returning <see langword="false"/>. Maps to
    /// 409; the caller should re-read and retry.</summary>
    public const string ConcurrencyConflict = "ApprovalPolicyConcurrencyConflict";
}
