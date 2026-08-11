using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>Just enough project context for <c>EvaluateEotCommandHandler</c> to confirm the project
/// exists (ADR-0002: tenant-scoped, so cross-tenant is indistinguishable from not-found) and default
/// the evaluation window when the caller does not supply one explicitly (domain-rules.md §1: "Default:
/// project start → data date").</summary>
public sealed record EotProjectContext(DateTimeOffset ContractStart, DateTimeOffset DataDate);

/// <summary>
/// Persistence boundary for S11-BE-02's <c>EvaluateEotCommand</c>. Bundles everything the evaluator
/// needs beyond the weather log history (<see cref="IDailyWeatherLogRepository"/>, reused as-is) and
/// the CPM run history (<see cref="ICpmRunHistoryReader"/>, reused as-is) - the project's default
/// calendar (domain-rules.md weather-eot §3.3), its <see cref="ProjectEotPolicy"/> if one has been
/// configured (§3.5), and the activity context §3.7/§5.4 need.
/// </summary>
public interface IEotEvaluationRepository
{
    /// <summary><see langword="null"/> when the project does not exist (or is not in the caller's
    /// tenant, which the global query filter makes indistinguishable from "does not exist" -
    /// ADR-0002).</summary>
    Task<EotProjectContext?> GetProjectContextAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>The project's <c>Calendar.IsDefault</c> row and its exceptions, or
    /// <see langword="null"/> when none is configured - domain-rules.md §3.3: "No default calendar
    /// ⟹ block, never guess", the caller's 422 <c>ProjectCalendarNotConfigured</c> gate.</summary>
    Task<EotCalendarContext?> GetDefaultCalendarAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary><see langword="null"/> when the project has no <see cref="ProjectEotPolicy"/> row -
    /// the caller substitutes <see cref="EotPolicySettings.Default"/> in that case (every field has a
    /// defensible, documented default; a missing row is not a configuration error, unlike the
    /// calendar above).</summary>
    Task<ProjectEotPolicy?> GetPolicyAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Just enough of each named <see cref="Activity"/> (scoped to this project) for §3.7's
    /// InWindow gate and §5.4's driver "who" columns - keyed by <c>ActivityId</c>, one entry per id in
    /// <paramref name="activityIds"/> that actually resolves.</summary>
    Task<IReadOnlyDictionary<Guid, EotActivityContext>> GetActivityContextsAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="evaluation"/> (and its owned <c>Runs</c>/<c>Sources</c>/<c>Drivers</c>
    /// children) as the only change in this operation, plus exactly one summarizing <see cref="AuditLog"/>
    /// row - domain-rules.md §2.1's zero-side-effect boundary (fixture W-14): nothing else is ever
    /// written by this call. Mirrors <c>CpmScheduleRepository.SaveResultsAsync</c>'s
    /// <c>SuppressPerEntityAudit</c> pattern so a large evaluation's <c>Driver</c>/<c>Source</c> rows
    /// do not each generate their own <see cref="AuditLog"/> row.
    /// </summary>
    Task SaveAsync(EotEvaluation evaluation, CancellationToken cancellationToken = default);
}
