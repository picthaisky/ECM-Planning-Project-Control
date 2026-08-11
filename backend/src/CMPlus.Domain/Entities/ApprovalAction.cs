using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Immutable, append-only ledger row: one human act against a Variation Order or Payment
/// Certificate approval chain (ADR-0008, approval-workflow.md §5.2/§6.6). Sprint 2 delivers only
/// this entity + its EF configuration - nothing constructs one yet, since the VO/Payment
/// Certificate state machines that would call <see cref="ApprovalAction"/>'s constructor land in
/// Sprint 9/10. Deliberately has no mutator method and no public property setter anywhere: a
/// correction is a new compensating row, never an edit (verified structurally by
/// <c>CMPlus.Domain.Tests</c>, not merely by convention).
/// </summary>
public sealed class ApprovalAction : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public ApprovalDocumentType DocumentType { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>1-based; bumped by <c>ReturnForRevision</c> (approval-workflow.md §4).</summary>
    public int RevisionNo { get; private set; }

    /// <summary>The chain step this action was taken against; 0 for actions that are not
    /// step-specific (e.g. <see cref="ApprovalActionType.Cancel"/> from <c>Draft</c>).</summary>
    public int StepNo { get; private set; }

    public Guid ActorUserId { get; private set; }

    public UserRole ActorRoleAtTime { get; private set; }

    public ApprovalActionType Action { get; private set; }

    /// <summary>Mandatory for <see cref="ApprovalActionType.Reject"/> and
    /// <see cref="ApprovalActionType.ReturnForRevision"/>, optional otherwise (approval-workflow.md §4).</summary>
    public string? Comment { get; private set; }

    public DateTimeOffset ActedAt { get; private set; }

    /// <summary>The policy version that routed the document at the time of this action
    /// (version-pin, approval-workflow.md §5.2).</summary>
    public Guid ApprovalPolicyId { get; private set; }

    public int ApprovalPolicyVersion { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ApprovalAction()
    {
    }

    public ApprovalAction(
        Guid tenantId,
        ApprovalDocumentType documentType,
        Guid documentId,
        int revisionNo,
        int stepNo,
        Guid actorUserId,
        UserRole actorRoleAtTime,
        ApprovalActionType action,
        string? comment,
        DateTimeOffset actedAt,
        Guid approvalPolicyId,
        int approvalPolicyVersion)
    {
        if (documentId == Guid.Empty)
        {
            throw new DomainException("ApprovalAction.DocumentId is required.");
        }

        if (revisionNo < 1)
        {
            throw new DomainException("ApprovalAction.RevisionNo must be 1 or greater.");
        }

        if (stepNo < 0)
        {
            throw new DomainException("ApprovalAction.StepNo cannot be negative.");
        }

        if ((action is ApprovalActionType.Reject or ApprovalActionType.ReturnForRevision) && string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException($"A comment is mandatory for {action}.");
        }

        TenantId = tenantId;
        DocumentType = documentType;
        DocumentId = documentId;
        RevisionNo = revisionNo;
        StepNo = stepNo;
        ActorUserId = actorUserId;
        ActorRoleAtTime = actorRoleAtTime;
        Action = action;
        Comment = comment;
        ActedAt = actedAt;
        ApprovalPolicyId = approvalPolicyId;
        ApprovalPolicyVersion = approvalPolicyVersion;
    }
}
