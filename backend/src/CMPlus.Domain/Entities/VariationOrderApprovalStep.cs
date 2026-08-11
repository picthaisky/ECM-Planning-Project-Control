using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One rung of a <see cref="VariationOrder"/>'s approval chain, snapshotted onto the document at
/// <see cref="VariationOrder.Submit"/> time - the exact <see cref="PaymentCertificateApprovalStep"/>
/// shape (S9 H-01 fix), reused unchanged per ADR-0008's Sprint 10 staging note. See that type's own
/// remarks for the full "why this exists" rationale; it applies verbatim here, including the one
/// extra reason domain-rules.md §2.1 calls out for the VO specifically: the cumulative-VO-escalation
/// step is <i>synthesised</i> at routing time with no real <see cref="ApprovalPolicyRule"/> row
/// behind it, so snapshotting is the only way to make it representable at decision time at all -
/// re-deriving "what does step 3 require" from the pinned policy would strand every escalated VO.
///
/// <para><b>Not append-only</b> - the same distinction <see cref="PaymentCertificateApprovalStep"/>
/// draws: <see cref="VariationOrder.ReturnForRevision"/>/<see cref="VariationOrder.Withdraw"/>
/// legitimately clear the whole snapshot so a resubmission can re-resolve a fresh chain (fixture
/// V-10). <see cref="INeverModified"/> still protects individual rungs from being edited in place -
/// only whole-set add/clear is legal.</para>
/// </summary>
public sealed class VariationOrderApprovalStep : Entity, ITenantOwned, INeverModified
{
    public Guid TenantId { get; private set; }

    public Guid VariationOrderId { get; private set; }

    /// <summary>Which submission's chain this rung belongs to - matches
    /// <see cref="VariationOrder.RevisionNo"/> at the moment <see cref="VariationOrder.Submit"/>
    /// resolved it.</summary>
    public int RevisionNo { get; private set; }

    /// <summary>1-based position in the resolved chain, exactly as the routing engine ordered it -
    /// including a synthesised escalation rung (domain-rules.md §4.1: appended at
    /// <c>StepNoMax + 1</c>).</summary>
    public int StepNo { get; private set; }

    public UserRole RequiredRole { get; private set; }

    /// <summary>Distinct-approver count required to clear this step (approval-workflow.md §6.2),
    /// snapshotted from the resolved chain step - never re-read from
    /// <see cref="ApprovalPolicyRule.QuorumCount"/> at decision time.</summary>
    public int QuorumCount { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private VariationOrderApprovalStep()
    {
    }

    internal VariationOrderApprovalStep(
        Guid tenantId,
        Guid variationOrderId,
        int revisionNo,
        int stepNo,
        UserRole requiredRole,
        int quorumCount)
    {
        if (variationOrderId == Guid.Empty)
        {
            throw new DomainException("VariationOrderApprovalStep.VariationOrderId is required.");
        }

        if (revisionNo < 1)
        {
            throw new DomainException("VariationOrderApprovalStep.RevisionNo must be 1 or greater.");
        }

        if (stepNo < 1)
        {
            throw new DomainException("VariationOrderApprovalStep.StepNo must be 1 or greater.");
        }

        if (quorumCount < 1)
        {
            throw new DomainException("VariationOrderApprovalStep.QuorumCount must be 1 or greater.");
        }

        TenantId = tenantId;
        VariationOrderId = variationOrderId;
        RevisionNo = revisionNo;
        StepNo = stepNo;
        RequiredRole = requiredRole;
        QuorumCount = quorumCount;
    }
}

/// <summary>
/// Plain data shape for one already-resolved chain rung, supplied to <see cref="VariationOrder.Submit"/> -
/// mirrors <see cref="PaymentCertificateApprovalStepInput"/> exactly (Domain-layer mirror of the
/// Application layer's resolved-chain-step shape, ADR-0001: Domain never references Application).
/// <c>SubmitVariationOrderCommandHandler</c> maps <c>IApprovalRoutingService</c>'s resolved steps
/// into this shape immediately before calling <see cref="VariationOrder.Submit"/>.
/// </summary>
public sealed record VariationOrderApprovalStepInput(int StepNo, UserRole RequiredRole, int QuorumCount);
