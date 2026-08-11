namespace CMPlus.Application.Abstractions;

/// <summary>
/// Request-time persistence boundary for <c>IdempotencyMiddleware</c> (S13-BE-01, ADR-0005 US-13.1).
/// Every method here is tenant-scoped through the ordinary EF Core global query filter (ADR-0002) -
/// unlike <see cref="IIdempotencyKeyMaintenance"/>, nothing on this interface ever needs to see
/// across tenants.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically (within this process - see <c>EfIdempotencyStore</c>'s remarks for what "atomic"
    /// means under the EF Core InMemory provider this environment is limited to) claims
    /// <paramref name="key"/> for the calling request, or reports why it cannot: the key already
    /// resolved to a completed response (<see cref="IdempotencyReservationOutcome.AlreadyCompleted"/>,
    /// replay it), the key is being processed by another in-flight request right now
    /// (<see cref="IdempotencyReservationOutcome.InProgressElsewhere"/>, 409), or the same key was
    /// supplied with a materially different request
    /// (<see cref="IdempotencyReservationOutcome.PayloadMismatch"/>, 409).
    /// </summary>
    Task<IdempotencyReservation> ReserveAsync(
        Guid tenantId,
        string key,
        string requestMethod,
        string requestPath,
        string requestHash,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Records the wrapped handler's response against the row <paramref name="idempotencyKeyId"/>
    /// identifies (returned by a <see cref="IdempotencyReservationOutcome.Reserved"/> reservation) so a
    /// future replay can serve it verbatim. Only ever called for a non-5xx outcome - see
    /// <see cref="ReleaseAsync"/> for the alternative.</summary>
    Task CompleteAsync(
        Guid idempotencyKeyId,
        int responseStatusCode,
        string? responseContentType,
        string? responseBody,
        bool responseNotReplayable,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Discards a reservation that never reached a cacheable outcome (the wrapped handler
    /// threw, or returned an unexpected &gt;= 500) - deletes the row outright rather than caching a
    /// transient failure, so the very next retry with the same key can attempt the real operation
    /// again instead of being memoized against a server error forever.</summary>
    Task ReleaseAsync(Guid idempotencyKeyId, CancellationToken cancellationToken = default);
}

public enum IdempotencyReservationOutcome
{
    /// <summary>No prior record existed (or the caller may proceed as if none did); the caller now
    /// owns <see cref="IdempotencyReservation.IdempotencyKeyId"/> and must eventually call
    /// <see cref="IIdempotencyStore.CompleteAsync"/> or <see cref="IIdempotencyStore.ReleaseAsync"/>.</summary>
    Reserved,

    /// <summary>A prior request already ran this exact key/payload to completion -
    /// <see cref="IdempotencyReservation"/>'s response fields carry what to replay verbatim. The
    /// wrapped handler must NOT run again - no second side effect, no second audit row.</summary>
    AlreadyCompleted,

    /// <summary>A prior request with this key is still in flight (this process, or - after a real
    /// unique-index violation on a real database - a concurrent request that lost the race to claim
    /// the row). The caller must not run the wrapped handler; 409, ask the client to retry shortly.</summary>
    InProgressElsewhere,

    /// <summary>This key was already used (completed or in flight) with a materially different
    /// request. The caller must not run the wrapped handler; 409.</summary>
    PayloadMismatch,
}

/// <summary>
/// <see cref="ResponseStatusCode"/>/<see cref="ResponseContentType"/>/<see cref="ResponseBody"/>/
/// <see cref="ResponseNotReplayable"/> are populated only for
/// <see cref="IdempotencyReservationOutcome.AlreadyCompleted"/>; <see cref="IdempotencyKeyId"/> only
/// for <see cref="IdempotencyReservationOutcome.Reserved"/>. Deliberately a single flat record rather
/// than a class-per-outcome hierarchy - every field's validity is fully determined by
/// <see cref="Outcome"/> alone, and the callers (one middleware) are all in one place.
/// </summary>
public sealed record IdempotencyReservation(
    IdempotencyReservationOutcome Outcome,
    Guid? IdempotencyKeyId = null,
    int? ResponseStatusCode = null,
    string? ResponseContentType = null,
    string? ResponseBody = null,
    bool ResponseNotReplayable = false)
{
    public static IdempotencyReservation Reserved(Guid idempotencyKeyId) =>
        new(IdempotencyReservationOutcome.Reserved, IdempotencyKeyId: idempotencyKeyId);

    public static IdempotencyReservation AlreadyCompleted(int responseStatusCode, string? responseContentType, string? responseBody, bool responseNotReplayable) =>
        new(
            IdempotencyReservationOutcome.AlreadyCompleted,
            ResponseStatusCode: responseStatusCode,
            ResponseContentType: responseContentType,
            ResponseBody: responseBody,
            ResponseNotReplayable: responseNotReplayable);

    public static readonly IdempotencyReservation InProgressElsewhere = new(IdempotencyReservationOutcome.InProgressElsewhere);

    public static readonly IdempotencyReservation PayloadMismatch = new(IdempotencyReservationOutcome.PayloadMismatch);
}
