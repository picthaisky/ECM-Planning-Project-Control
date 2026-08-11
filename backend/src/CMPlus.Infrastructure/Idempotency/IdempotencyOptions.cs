namespace CMPlus.Infrastructure.Idempotency;

/// <summary>
/// Bound from the <c>Idempotency</c> configuration section (S13-BE-01/S13-DB-01) - same "read the
/// cap from config, don't hardcode" discipline as <c>PhotoOptions</c>/<c>ImportOptions</c>.
///
/// <para><b>Retention policy (S13-DB-01 DoD: "นโยบายเก็บรักษาที่บันทึกไว้" - documented retention
/// policy).</b> <see cref="CompletedRetention"/> defaults to 90 days. This is a judgement call, not
/// a formula, made against real evidence already in this codebase rather than picked arbitrarily:
/// the offline outbox itself has NO client-side expiry on an un-synced item - <c>web/src/services/
/// outbox/outboxMaintenance.ts</c> only purges <i>synced</i> records (after 7 days), and its own test
/// fixtures (<c>outboxMaintenance.test.ts</c>) deliberately keep 90-day-old <c>failed</c>/<c>queued</c>
/// items unpurged as the "still there, still retryable" baseline. A device that reconnects within 90
/// days therefore replays cleanly against a live key; the residual risk - a device that stays offline
/// longer than that and then flushes a stale queue - is real but irreducible from the server side
/// alone (the client itself places no outer bound on how long it will keep retrying), so this value
/// is chosen to match the exact worst case the client's own tests already treat as "normal", not to
/// eliminate the risk entirely. <see cref="InProgressRetention"/> is deliberately much shorter (1
/// day): a reservation that never reached <see cref="Domain.Enums.IdempotencyRequestStatus.Completed"/>
/// within a day is not "still working", it is an abandoned row (the owning process crashed, or the
/// request never returned to complete/release it) and should stop shadowing a legitimate retry.
/// </para>
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a <see cref="Domain.Enums.IdempotencyRequestStatus.Completed"/> row is kept
    /// before <see cref="EfIdempotencyStore.PurgeExpiredAsync"/> deletes it. See this type's class
    /// remarks for why 90 days.</summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How long an abandoned <see cref="Domain.Enums.IdempotencyRequestStatus.InProgress"/>
    /// row is kept before the sweep reclaims it. See this type's class remarks.</summary>
    public TimeSpan InProgressRetention { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How often <see cref="IdempotencyKeyCleanupService"/> runs the sweep. An hour is
    /// generous headroom below both retention windows above (particularly
    /// <see cref="InProgressRetention"/>) while never running so often that it meaningfully competes
    /// with request-time traffic for the same table.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>S13-BE-01 design note: "bounded - do not store megabyte photo responses". None of the
    /// four endpoints this middleware currently wraps can approach this (each returns a small JSON
    /// DTO, never raw file bytes - see <c>IdempotencyMiddleware</c>'s class remarks); this exists as a
    /// structural backstop for whatever gets wrapped next, not a limit any current response is
    /// expected to test.</summary>
    public int MaxReplayableResponseBodyBytes { get; set; } = 64 * 1024;
}
