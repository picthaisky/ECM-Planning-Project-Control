using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// The amount-tiered Delegation-of-Authority policy for one document type, tenant-wide
/// (<see cref="ProjectId"/> null) or project-scoped override (ADR-0008, approval-workflow.md
/// §5.2). Immutable once created: editing a policy never mutates rows in place, it always
/// produces a new <see cref="ApprovalPolicy"/> instance with <c>Version + 1</c>
/// (<see cref="CreateNextVersion"/>) - the caller is responsible for deactivating the previous
/// version (<see cref="Deactivate"/>) and persisting both, so in-flight documents that pinned the
/// old version remain fully explainable (approval-workflow.md §5.2 "version-pin, never mutate").
///
/// Rule-band validation (non-overlapping, gap-free per <c>StepNo</c>) runs once at construction
/// time over the *combination* of every rule supplied - not on individual rows - because the
/// invariant is about how the bands compose into a chain for any given amount, not about any
/// single row in isolation. See <see cref="ValidateBands"/> for the exact rule: bands are
/// evaluated as atomic amount intervals; every interval that at least one rule claims to cover
/// must resolve to a contiguous StepNo sequence starting at 1 (no gap, no overlap/duplicate
/// StepNo). An amount interval nobody claims at all is permitted at save time (a deliberately
/// sparse policy, e.g. approval-workflow.md §8 fixture R5) - that is a fail-closed
/// <c>ApprovalPolicyGap</c> at *routing* time (<c>IApprovalRoutingService</c>), not a save-time
/// rejection; save-time rejection is reserved for bands that are actually broken (overlapping or
/// skipping a StepNo where something already claims to apply).
/// </summary>
public sealed class ApprovalPolicy : Entity, ITenantOwned
{
    private readonly List<ApprovalPolicyRule> _rules = [];

    public Guid TenantId { get; private set; }

    /// <summary><c>null</c> = tenant-wide default; non-null = project-specific override
    /// (schema-present, not surfaced until Sprint 15 per ADR-0008 consequences).</summary>
    public Guid? ProjectId { get; private set; }

    public ApprovalDocumentType DocumentType { get; private set; }

    /// <summary>Bumped on every edit; never reused. Documents pin the version that routed them.</summary>
    public int Version { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public bool AllowSelfApproval { get; private set; }

    /// <summary>VO policies only. <c>null</c> disables cumulative escalation entirely.</summary>
    public decimal? CumulativeVoEscalationPct { get; private set; }

    public UserRole? CumulativeVoEscalationRole { get; private set; }

    public IReadOnlyList<ApprovalPolicyRule> Rules => _rules.AsReadOnly();

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ApprovalPolicy()
    {
    }

    private ApprovalPolicy(
        Guid tenantId,
        Guid? projectId,
        ApprovalDocumentType documentType,
        int version,
        DateTimeOffset effectiveFrom,
        bool allowSelfApproval,
        decimal? cumulativeVoEscalationPct,
        UserRole? cumulativeVoEscalationRole,
        IReadOnlyList<ApprovalPolicyRuleInput> rules)
    {
        if (version < 1)
        {
            throw new DomainException("ApprovalPolicy.Version must be 1 or greater.");
        }

        if (cumulativeVoEscalationPct is < 0 or > 100)
        {
            throw new DomainException("ApprovalPolicy.CumulativeVoEscalationPct must be between 0 and 100 when supplied.");
        }

        ValidateBands(rules);

        TenantId = tenantId;
        ProjectId = projectId;
        DocumentType = documentType;
        Version = version;
        IsActive = true;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = null;
        AllowSelfApproval = allowSelfApproval;
        CumulativeVoEscalationPct = cumulativeVoEscalationPct;
        CumulativeVoEscalationRole = cumulativeVoEscalationRole;

        foreach (var rule in rules)
        {
            _rules.Add(new ApprovalPolicyRule(
                tenantId, Id, rule.StepNo, rule.MinAmount, rule.MaxAmount,
                rule.RequiredRole, rule.RequiredUserId, rule.QuorumCount));
        }
    }

    public static ApprovalPolicy CreateInitialVersion(
        Guid tenantId,
        Guid? projectId,
        ApprovalDocumentType documentType,
        DateTimeOffset effectiveFrom,
        IReadOnlyList<ApprovalPolicyRuleInput> rules,
        bool allowSelfApproval = false,
        decimal? cumulativeVoEscalationPct = null,
        UserRole? cumulativeVoEscalationRole = null) =>
        new(tenantId, projectId, documentType, version: 1, effectiveFrom, allowSelfApproval,
            cumulativeVoEscalationPct, cumulativeVoEscalationRole, rules);

    /// <summary>
    /// Produces the next version as a brand-new instance (new <c>Id</c>, <c>Version + 1</c>).
    /// Does NOT deactivate <c>this</c> - the caller must also call <see cref="Deactivate"/> on the
    /// current instance and persist both rows in the same transaction (approval-workflow.md §5.2).
    /// </summary>
    public ApprovalPolicy CreateNextVersion(
        DateTimeOffset effectiveFrom,
        IReadOnlyList<ApprovalPolicyRuleInput> rules,
        bool allowSelfApproval,
        decimal? cumulativeVoEscalationPct,
        UserRole? cumulativeVoEscalationRole) =>
        new(TenantId, ProjectId, DocumentType, Version + 1, effectiveFrom, allowSelfApproval,
            cumulativeVoEscalationPct, cumulativeVoEscalationRole, rules);

    public void Deactivate(DateTimeOffset effectiveTo)
    {
        IsActive = false;
        EffectiveTo = effectiveTo;
    }

    /// <summary>
    /// Validates that the supplied rule bands, taken together, are non-overlapping and gap-free
    /// per StepNo (approval-workflow.md §5.2/§5.3 step 7; design.md §1.2). Algorithm: collect
    /// every distinct MinAmount/MaxAmount value across all rules as boundary points; for each
    /// atomic interval between two consecutive boundary points (plus the unbounded tail beyond
    /// the last one), determine which rules fully cover it, and require that set of StepNos -
    /// when non-empty - to be exactly {1, ..., k} with no gap and no duplicate/overlapping
    /// StepNo. An interval nobody covers at all is allowed (see class remarks).
    /// </summary>
    private static void ValidateBands(IReadOnlyList<ApprovalPolicyRuleInput> rules)
    {
        if (rules.Count == 0)
        {
            throw new DomainException("An ApprovalPolicy must define at least one rule.");
        }

        var boundaries = new SortedSet<decimal>();
        foreach (var rule in rules)
        {
            boundaries.Add(rule.MinAmount);
            if (rule.MaxAmount is { } max)
            {
                boundaries.Add(max);
            }
        }

        var points = boundaries.ToList();

        for (var i = 0; i < points.Count - 1; i++)
        {
            AssertContiguousStepsAt(rules, points[i], points[i + 1]);
        }

        // The unbounded tail beyond the last referenced boundary point - only meaningful when at
        // least one rule is itself unbounded (MaxAmount null); AssertContiguousStepsAt handles the
        // "nobody covers it" case gracefully either way.
        AssertContiguousStepsAt(rules, points[^1], upper: null);
    }

    private static void AssertContiguousStepsAt(IReadOnlyList<ApprovalPolicyRuleInput> rules, decimal lower, decimal? upper)
    {
        var covering = rules
            .Where(r => r.MinAmount <= lower && (upper is null ? r.MaxAmount is null : (r.MaxAmount is null || r.MaxAmount >= upper.Value)))
            .Select(r => r.StepNo)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        for (var expected = 1; expected <= covering.Count; expected++)
        {
            if (covering[expected - 1] != expected)
            {
                var rangeDescription = upper is null ? $"{lower:0.00} and above" : $"[{lower:0.00}, {upper.Value:0.00})";
                throw new DomainException(
                    $"ApprovalPolicyRule bands are not a contiguous StepNo sequence for the amount range " +
                    $"{rangeDescription}: resolved steps are [{string.Join(",", covering)}], missing StepNo {expected}.");
            }
        }
    }
}
