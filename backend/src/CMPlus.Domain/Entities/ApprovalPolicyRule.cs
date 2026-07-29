using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One amount-tiered rung of an <see cref="ApprovalPolicy"/> (ADR-0008, approval-workflow.md
/// §5.2). <see cref="MinAmount"/> is inclusive, <see cref="MaxAmount"/> exclusive
/// (<c>null</c> = unbounded). The constructor is <c>internal</c>: rules are only ever created as
/// part of building an <see cref="ApprovalPolicy"/> version (via
/// <see cref="ApprovalPolicy.CreateInitialVersion"/>/<see cref="ApprovalPolicy.CreateNextVersion"/>),
/// never mutated afterward - a policy edit creates a whole new version with its own new rule
/// rows (version-pin, approval-workflow.md §5.2).
/// </summary>
public sealed class ApprovalPolicyRule : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ApprovalPolicyId { get; private set; }

    /// <summary>1-based position in the chain; ties within the same amount range are invalid
    /// (validated by <see cref="ApprovalPolicy"/> at construction, not here).</summary>
    public int StepNo { get; private set; }

    /// <summary>Inclusive lower bound of the amount band this rule applies to.</summary>
    public decimal MinAmount { get; private set; }

    /// <summary>Exclusive upper bound; <c>null</c> = unbounded.</summary>
    public decimal? MaxAmount { get; private set; }

    public UserRole RequiredRole { get; private set; }

    /// <summary>Named-person override; schema-present but not surfaced in the Sprint 2 write
    /// surface (ADR-0008 consequences) - use sparingly, prefer <see cref="RequiredRole"/>.</summary>
    public Guid? RequiredUserId { get; private set; }

    /// <summary>Distinct-approver count required to clear this step; schema-present but the
    /// Sprint 2 engine/UI only ever sets/reads 1 (ADR-0008 consequences: "QuorumCount &gt; 1 -
    /// engine supports it, UI not yet open").</summary>
    public int QuorumCount { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ApprovalPolicyRule()
    {
    }

    internal ApprovalPolicyRule(
        Guid tenantId,
        Guid approvalPolicyId,
        int stepNo,
        decimal minAmount,
        decimal? maxAmount,
        UserRole requiredRole,
        Guid? requiredUserId,
        int quorumCount)
    {
        if (stepNo < 1)
        {
            throw new DomainException("ApprovalPolicyRule.StepNo must be 1 or greater.");
        }

        if (minAmount < 0)
        {
            throw new DomainException($"ApprovalPolicyRule (StepNo {stepNo}): MinAmount cannot be negative.");
        }

        if (maxAmount is { } max && max <= minAmount)
        {
            throw new DomainException(
                $"ApprovalPolicyRule (StepNo {stepNo}): MaxAmount must be greater than MinAmount when supplied.");
        }

        if (quorumCount < 1)
        {
            throw new DomainException($"ApprovalPolicyRule (StepNo {stepNo}): QuorumCount must be 1 or greater.");
        }

        TenantId = tenantId;
        ApprovalPolicyId = approvalPolicyId;
        StepNo = stepNo;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        RequiredRole = requiredRole;
        RequiredUserId = requiredUserId;
        QuorumCount = quorumCount;
    }
}
