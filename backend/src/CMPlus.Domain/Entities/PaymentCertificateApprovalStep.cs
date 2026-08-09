using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One rung of a <see cref="PaymentCertificate"/>'s approval chain, snapshotted onto the document at
/// <see cref="PaymentCertificate.Submit"/> time from whatever the routing engine actually resolved
/// for THIS document's specific routing amount (security review sprint-09.md H-01 fix).
///
/// <para><b>Why this exists.</b> Before this fix, Approve/Reject/ReturnForRevision re-derived "what
/// role does step N require" from the <i>entire</i> rule set of the pinned
/// <see cref="ApprovalPolicy"/>, ignoring the amount band that routed the document
/// (<c>PinnedApprovalChainResolver</c>, now deleted). When two rules shared a <c>StepNo</c> across
/// different amount bands with different roles - an entirely ordinary tiered DoA, e.g. QS below
/// ฿1,000,000 and PM at or above it, both at step 1 - the wrong band's rule could win, letting an
/// out-of-band actor clear a step reserved for someone else. Snapshotting the chain exactly as
/// resolved removes the whole re-derivation bug class: Approve/Reject/ReturnForRevision now read
/// only <see cref="PaymentCertificate.ApprovalSteps"/>, never the policy's full rule set, so there is
/// nothing left to re-derive incorrectly. It also makes the Sprint 10 cumulative-VO-escalation step -
/// synthesised at routing time with no real <see cref="ApprovalPolicyRule"/> row behind it - fully
/// representable, since it is snapshotted exactly like any other resolved step.</para>
///
/// <para><b>Not append-only.</b> Unlike <see cref="ApprovalAction"/>, this is working state, not
/// legal evidence: <see cref="PaymentCertificate.ReturnForRevision"/>/<see cref="PaymentCertificate.Withdraw"/>
/// legitimately clear it (voiding the chain snapshot) and a resubmission re-populates it fresh,
/// possibly with a different shape if the claimed amount changed (approval-workflow.md §5.3 step 8).
/// <see cref="RevisionNo"/> distinguishes which submission a given rung belongs to, so more than one
/// revision's steps can coexist historically for the same certificate row.</para>
/// </summary>
public sealed class PaymentCertificateApprovalStep : Entity, ITenantOwned, INeverModified
{
    public Guid TenantId { get; private set; }

    public Guid PaymentCertificateId { get; private set; }

    /// <summary>Which submission's chain this rung belongs to - matches
    /// <see cref="PaymentCertificate.RevisionNo"/> at the moment <see cref="PaymentCertificate.Submit"/>
    /// resolved it.</summary>
    public int RevisionNo { get; private set; }

    /// <summary>1-based position in the resolved chain, exactly as the routing engine ordered it.</summary>
    public int StepNo { get; private set; }

    public UserRole RequiredRole { get; private set; }

    /// <summary>Distinct-approver count required to clear this step (approval-workflow.md §6.2),
    /// snapshotted from the resolved <c>ApprovalChainStep</c> - never re-read from
    /// <see cref="ApprovalPolicyRule.QuorumCount"/> at decision time (security review sprint-09.md
    /// H-02 fix: enforced by <c>ApprovePaymentCertificateCommandHandler</c> against this value).</summary>
    public int QuorumCount { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private PaymentCertificateApprovalStep()
    {
    }

    internal PaymentCertificateApprovalStep(
        Guid tenantId,
        Guid paymentCertificateId,
        int revisionNo,
        int stepNo,
        UserRole requiredRole,
        int quorumCount)
    {
        if (paymentCertificateId == Guid.Empty)
        {
            throw new DomainException("PaymentCertificateApprovalStep.PaymentCertificateId is required.");
        }

        if (revisionNo < 1)
        {
            throw new DomainException("PaymentCertificateApprovalStep.RevisionNo must be 1 or greater.");
        }

        if (stepNo < 1)
        {
            throw new DomainException("PaymentCertificateApprovalStep.StepNo must be 1 or greater.");
        }

        if (quorumCount < 1)
        {
            throw new DomainException("PaymentCertificateApprovalStep.QuorumCount must be 1 or greater.");
        }

        TenantId = tenantId;
        PaymentCertificateId = paymentCertificateId;
        RevisionNo = revisionNo;
        StepNo = stepNo;
        RequiredRole = requiredRole;
        QuorumCount = quorumCount;
    }
}

/// <summary>
/// Plain data shape for one already-resolved chain rung, supplied to <see cref="PaymentCertificate.Submit"/> -
/// mirrors <see cref="ApprovalPolicyRuleInput"/>'s "input shape before it becomes a child entity"
/// pattern. This is the Domain-layer mirror of the Application layer's own resolved-chain-step shape
/// (ADR-0001: Domain never references Application) - <c>SubmitPaymentCertificateCommandHandler</c>
/// maps the routing engine's resolved steps into this shape immediately before calling
/// <see cref="PaymentCertificate.Submit"/>.
/// </summary>
public sealed record PaymentCertificateApprovalStepInput(int StepNo, UserRole RequiredRole, int QuorumCount);
