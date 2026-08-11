using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// domain-rules.md (manpower-equipment) §4.6(a): the editable, audited counterpart to
/// <see cref="ManpowerEquipmentLog"/>'s immutable actuals - "manning plans are revised weekly, that
/// is what planning is". Deliberately NOT on the append-only log row (§4.6's rejection of
/// <c>docs/9.</c> §4's <c>PlannedManCount</c> field: "putting a mutable value on an append-only row
/// forces one of two bad outcomes: either the plan can never be revised, or the row is not really
/// append-only").
///
/// <para>Ordinary mutable aggregate (unlike its sibling) - every change still writes an
/// <see cref="AuditLog"/> row via the default <c>AuditSaveChangesInterceptor</c> behaviour (CLAUDE.md:
/// "every mutating domain operation writes an audit log entry"), it is simply not
/// <see cref="Common.IAppendOnly"/>: a plan is a live configuration, not legal evidence of what
/// happened on site.</para>
///
/// <para><b>Never seeded with a placeholder (ADR-0015 discipline).</b> <see cref="PlannedWorkerCount"/>/
/// <see cref="PlannedManHours"/> are both nullable and <see langword="null"/> means "no manning plan
/// for this scope/period", never 0 - a seeded 0 would be indistinguishable from a genuine "zero
/// workers planned" decision once in production data.</para>
/// </summary>
public sealed class ManpowerPlan : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? WorkCategoryId { get; private set; }

    public Guid? WbsNodeId { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset EffectiveTo { get; private set; }

    /// <summary><see langword="null"/> = no manning plan (never 0 - ADR-0015 discipline).</summary>
    public int? PlannedWorkerCount { get; private set; }

    /// <summary><c>decimal(9,2)</c>. <see langword="null"/> = no manning plan (never 0.00).</summary>
    public decimal? PlannedManHours { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ManpowerPlan()
    {
    }

    public ManpowerPlan(
        Guid tenantId,
        Guid projectId,
        Guid? workCategoryId,
        Guid? wbsNodeId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveTo,
        int? plannedWorkerCount,
        decimal? plannedManHours)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("ManpowerPlan.ProjectId is required.");
        }

        if (effectiveTo < effectiveFrom)
        {
            throw new DomainException("ManpowerPlan.EffectiveTo cannot be earlier than EffectiveFrom.");
        }

        ValidatePlannedWorkerCount(plannedWorkerCount);
        ValidatePlannedManHours(plannedManHours);

        TenantId = tenantId;
        ProjectId = projectId;
        WorkCategoryId = workCategoryId;
        WbsNodeId = wbsNodeId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        PlannedWorkerCount = plannedWorkerCount;
        PlannedManHours = plannedManHours;
    }

    /// <summary>Revises the plan in place - the whole point of this entity being mutable, unlike
    /// <see cref="ManpowerEquipmentLog"/> (§4.6(a)). Every call is a distinct mutation the default
    /// audit interceptor records.</summary>
    public void Revise(
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveTo,
        int? plannedWorkerCount,
        decimal? plannedManHours)
    {
        if (effectiveTo < effectiveFrom)
        {
            throw new DomainException("ManpowerPlan.EffectiveTo cannot be earlier than EffectiveFrom.");
        }

        ValidatePlannedWorkerCount(plannedWorkerCount);
        ValidatePlannedManHours(plannedManHours);

        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        PlannedWorkerCount = plannedWorkerCount;
        PlannedManHours = plannedManHours;
    }

    private static void ValidatePlannedWorkerCount(int? plannedWorkerCount)
    {
        if (plannedWorkerCount is < 0)
        {
            throw new DomainException("ManpowerPlan.PlannedWorkerCount cannot be negative.");
        }
    }

    private static void ValidatePlannedManHours(decimal? plannedManHours)
    {
        if (plannedManHours is < 0)
        {
            throw new DomainException("ManpowerPlan.PlannedManHours cannot be negative.");
        }
    }
}
