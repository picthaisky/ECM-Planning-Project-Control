using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One precedence relation within a captured <see cref="CpmRun"/>'s network topology (ADR-0019,
/// domain-rules.md weather-eot §4.3) - mirrors <see cref="ActivityRelation"/>'s own four fields
/// exactly. Captured because the not-yet-built EotEvaluator (S11-BE-02) re-runs
/// <c>CMPlus.Application.Services.Cpm.CpmEngine</c> against the governing run's network rather than
/// trusting stored per-activity flags (domain-rules.md §4.3: "not optional" - "omitting it would
/// force the evaluator to trust stored per-activity flags, which is the very thing the
/// contemporaneous ruling exists to avoid").
///
/// <para><see cref="IAppendOnly"/> - see <see cref="CpmRunActivity"/>'s identical remarks. The
/// constructor is <c>internal</c> so the only way to obtain an instance is through
/// <see cref="CpmRun.Capture"/>.</para>
/// </summary>
public sealed class CpmRunRelation : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid CpmRunId { get; private set; }

    public Guid PredecessorActivityId { get; private set; }

    public Guid SuccessorActivityId { get; private set; }

    public RelationType RelationType { get; private set; }

    public int LagDays { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private CpmRunRelation()
    {
    }

    internal CpmRunRelation(Guid tenantId, Guid cpmRunId, CpmRunRelationInput input)
    {
        if (cpmRunId == Guid.Empty)
        {
            throw new DomainException("CpmRunRelation.CpmRunId is required.");
        }

        if (input.PredecessorActivityId == input.SuccessorActivityId)
        {
            throw new DomainException("CpmRunRelation cannot relate an activity to itself.");
        }

        TenantId = tenantId;
        CpmRunId = cpmRunId;
        PredecessorActivityId = input.PredecessorActivityId;
        SuccessorActivityId = input.SuccessorActivityId;
        RelationType = input.RelationType;
        LagDays = input.LagDays;
    }
}
