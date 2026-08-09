using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for S7-BE-05's <c>CloseEvmPeriodCommand</c>. Deliberately narrow - the
/// command handler resolves the actual PV/EV/AC/EAC figures itself via
/// <see cref="Features.Evm.EvmComputationService"/> (the same computation `GetEvmQuery` uses, per
/// docs/10 §7 S8-BE-01's later "no calculation outside the EVM engine" rule, honoured a sprint early
/// here) and only reaches this interface to check-then-insert the resulting
/// <see cref="EvmPeriodSnapshot"/>.
/// </summary>
public interface IEvmSnapshotRepository
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002).</summary>
    Task<bool> ExistsForDataDateAsync(Guid projectId, DateTimeOffset dataDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="snapshot"/> as the only change in this operation - the default
    /// one-row-per-changed-entity <c>AuditSaveChangesInterceptor</c> behaviour is exactly right here
    /// (S7-BE-05 is a single-row mutation, not a bulk operation), producing one `Created` audit row.
    /// Callers must have already checked <see cref="ExistsForDataDateAsync"/> in the same request -
    /// this method itself does not re-check (the app-level check plus the DB's own unique index on
    /// `(TenantId, ProjectId, DataDate)`, design.md §3, are the two layers of defense against a
    /// duplicate; a race between the two concurrent requests is a real SQL Server unique-constraint
    /// violation this sprint cannot verify without the Docker/MSSQL outage being resolved - see the
    /// Sprint 7 backend-developer report).
    /// </summary>
    Task AddAsync(EvmPeriodSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closed periods for one project, ordered by <c>DataDate</c> ascending, optionally bounded by
    /// an inclusive <paramref name="from"/>/<paramref name="to"/> window. Tenant-scoped by the
    /// global EF query filter (ADR-0002).
    ///
    /// <para>This read exists because S7-FE-04's DoD requires the S-Curve's points *before* the data
    /// date to come from frozen <see cref="EvmPeriodSnapshot"/> rows rather than being recomputed
    /// live — which is ADR-0009's whole point: a backdated <c>ActivityProgressLog</c> entry
    /// legitimately changes a live recomputation, so anything that must stay reproducible (a
    /// dashboard, an exported PDF, a payment certificate) has to cite a snapshot instead. Ascending
    /// order is deliberate: it is chart-plot order, the only consumer this serves.</para>
    /// </summary>
    Task<IReadOnlyList<EvmPeriodSnapshot>> ListAsync(
        Guid projectId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
