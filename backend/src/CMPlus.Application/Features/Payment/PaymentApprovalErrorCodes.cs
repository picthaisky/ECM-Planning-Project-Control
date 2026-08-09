namespace CMPlus.Application.Features.Payment;

/// <summary>
/// Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the S9-BE-05 Payment
/// Certificate approval-workflow commands - distinct from <c>CMPlus.Application.Services.Payment.PaymentErrorCodes</c>,
/// which covers the S9-BE-02/03 money-math calculators. <c>CMPlus.Application.Approval.ApprovalErrorCodes.PolicyGap</c>
/// (422, already mapped by <c>ResultProblemMapper</c> since Sprint 2) is reused as-is for "chain
/// resolved empty" and is not duplicated here.
/// </summary>
public static class PaymentApprovalErrorCodes
{
    /// <summary>No <see cref="CMPlus.Domain.Entities.PaymentCertificate"/> with this id exists in the
    /// caller's tenant (or at all) - the global EF query filter (ADR-0002) makes "wrong tenant" and
    /// "does not exist" indistinguishable, closing the certificate-id IDOR concern S9-SEC-01 must
    /// verify.</summary>
    public const string NotFound = "PaymentCertificateNotFound";

    /// <summary>The certificate's current <c>Status</c> does not allow the requested transition
    /// (e.g. <c>Approve</c> on a certificate that is not <c>PendingApproval</c>). Maps to
    /// <c>409 document-immutable</c> (design.md §2.3) - the document is not currently open to the
    /// action being attempted.</summary>
    public const string InvalidStatusForTransition = "PaymentCertificateInvalidStatusForTransition";

    /// <summary>
    /// The caller does not currently hold approval authority over this document: on
    /// <c>Approve</c>, their role does not match <c>CurrentStepNo</c>'s required role; on
    /// <c>ReturnForRevision</c>, their role does not match <i>any</i> pending step's required role
    /// (approval-workflow.md §4: "actor holds any pending step's role"); on <c>Reject</c>, either
    /// the certificate is not yet at its final step or their role does not match the final step's
    /// required role (approval-workflow.md §4/§6.1: "only the final step's approver may reject").
    /// One shared code for all three, matching design.md §2.3's single <c>403 not-current-step</c>
    /// type - this is deliberately never satisfied by a static role check (S9-BE-05 DoD: "no escape
    /// hatch"); it is always resolved from the document's own version-pinned chain.
    /// </summary>
    public const string NotAuthorizedForApprovalStep = "PaymentCertificateNotAuthorizedForApprovalStep";

    /// <summary>The actor is the certificate's creator/submitter and the pinned policy's
    /// <c>AllowSelfApproval</c> is <see langword="false"/> (approval-workflow.md §6.1). Maps to
    /// <c>403 self-approval-not-permitted</c> (design.md §2.3) - the exact code fixture R10 exercises.</summary>
    public const string SelfApprovalNotPermitted = "PaymentCertificateSelfApprovalNotPermitted";

    /// <summary>The actor already approved a different step of this same document revision
    /// (approval-workflow.md §6.1: "a single user may not satisfy two steps of the same chain even
    /// if they hold both roles - each step needs a distinct human"). Maps to 403.</summary>
    public const string DuplicateChainApprover = "PaymentCertificateDuplicateChainApprover";

    /// <summary>A concurrent writer already changed this exact certificate row
    /// (<c>PaymentCertificate.RowVersion</c> mismatch) between this request's load and its save.
    /// Maps to <c>409 concurrent-transition</c> (design.md §2.3) - never a silent double-advance
    /// through the chain (S9-BE-01 DoD).</summary>
    public const string ConcurrencyConflict = "PaymentCertificateConcurrencyConflict";

    /// <summary>
    /// The certificate's snapshotted <see cref="CMPlus.Domain.Entities.PaymentCertificate.ApprovalSteps"/>
    /// has no rung for the step being acted on (<c>CurrentStepNo</c> for Approve/ReturnForRevision,
    /// <c>TotalSteps</c> for Reject). Should be unreachable now that
    /// <c>ApprovalPolicy.ValidateBands</c> rejects any rule set that could ever produce such a chain
    /// before it is saved (security review sprint-09.md M-03) - kept as a mapped <c>Result</c>
    /// failure rather than an unhandled throw so a corrupt/legacy chain degrades to a clear 409
    /// instead of an unrecoverable 500 (M-03's second half: "convert the throw into a mapped Result
    /// failure").</summary>
    public const string CorruptApprovalChain = "PaymentCertificateCorruptApprovalChain";
}
