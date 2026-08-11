using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One governing <see cref="CpmRun"/>'s contribution to an <see cref="EotEvaluation"/>
/// (domain-rules.md weather-eot §5.1) - "one TIA per governing run, never per arbitrary date slice".
/// The overwhelmingly common case (W-01 through W-14, W-16) produces exactly one of these; W-15's
/// contemporaneous-windows fixture is the one that needs two, each with its own
/// <see cref="AsScheduledDurationDays"/>/<see cref="ImpactedDurationDays"/>/<see cref="DeltaDays"/> -
/// which is why those three live here, per-run, rather than only on the parent aggregate.
///
/// <para><see cref="IAppendOnly"/> - same structural guarantee as every other child of an append-only
/// aggregate in this codebase (<see cref="CpmRunActivity"/>, <see cref="DailyWeatherLogActivity"/>).
/// The constructor is <c>internal</c> so the only way to obtain an instance is through
/// <see cref="EotEvaluation.Capture"/>.</para>
/// </summary>
public sealed class EotEvaluationRun : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid EotEvaluationId { get; private set; }

    public Guid CpmRunId { get; private set; }

    /// <summary>The earliest countable date this run governed, within this evaluation.</summary>
    public DateTimeOffset WindowFrom { get; private set; }

    /// <summary>The latest countable date this run governed, within this evaluation.</summary>
    public DateTimeOffset WindowTo { get; private set; }

    /// <summary>$D^{as}_r$ - <c>CpmEngine.Calculate</c> over this run's own, un-impacted network.</summary>
    public int AsScheduledDurationDays { get; private set; }

    /// <summary>$D^{imp}_r$ - the same network with this run's countable stoppages added.</summary>
    public int ImpactedDurationDays { get; private set; }

    /// <summary>$\max(0, D^{imp}_r - D^{as}_r)$ - this run's own contribution to $E$.</summary>
    public int DeltaDays { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private EotEvaluationRun()
    {
    }

    internal EotEvaluationRun(Guid tenantId, Guid eotEvaluationId, EotEvaluationRunInput input)
    {
        if (eotEvaluationId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationRun.EotEvaluationId is required.");
        }

        if (input.CpmRunId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationRun.CpmRunId is required.");
        }

        if (input.WindowTo < input.WindowFrom)
        {
            throw new DomainException("EotEvaluationRun.WindowTo cannot precede WindowFrom.");
        }

        if (input.AsScheduledDurationDays < 0 || input.ImpactedDurationDays < 0 || input.DeltaDays < 0)
        {
            throw new DomainException("EotEvaluationRun duration fields cannot be negative.");
        }

        TenantId = tenantId;
        EotEvaluationId = eotEvaluationId;
        CpmRunId = input.CpmRunId;
        WindowFrom = input.WindowFrom;
        WindowTo = input.WindowTo;
        AsScheduledDurationDays = input.AsScheduledDurationDays;
        ImpactedDurationDays = input.ImpactedDurationDays;
        DeltaDays = input.DeltaDays;
    }
}
