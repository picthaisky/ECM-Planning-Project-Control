using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Server-side dedupe record for an <c>Idempotency-Key</c> header on a site-module write endpoint
/// (S13-BE-01/S13-DB-01, ADR-0005 US-13.1). Closes security review sprint-12.md M-01: the offline
/// outbox's <c>reconcileInterruptedSyncs</c> can replay an upload whose response the client never
/// saw even though the server already committed it - this is what makes the replay a no-op instead
/// of a second <see cref="Photo"/>/<see cref="DailyWeatherLog"/> row.
///
/// <para><b>Not <see cref="Common.IAppendOnly"/> or <see cref="Common.INeverModified"/>, unlike most
/// of this schema's site-log entities.</b> This is a purely technical HTTP-layer dedupe cache, not
/// legal/business evidence (contrast <see cref="DailyWeatherLog"/>) - it legitimately transitions
/// <see cref="IdempotencyRequestStatus.InProgress"/> -&gt; <see cref="IdempotencyRequestStatus.Completed"/>
/// exactly once via <see cref="Complete"/>, and a row that never reaches that transition (the
/// request's owning process crashed, or the underlying operation failed with an unexpected server
/// error) is deleted outright by <c>EfIdempotencyStore.ReleaseAsync</c>/the retention sweep rather
/// than kept as a tombstone - see db-conventions.md §7's append-only table list, which this
/// deliberately does not join.</para>
///
/// <para><b>Why no <see cref="Domain.Entities.PaymentCertificate.RowVersion"/>-style optimistic
/// concurrency token.</b> The one genuine multi-writer race - two concurrent requests both trying to
/// claim the same brand-new <c>(TenantId, Key)</c> - is a concurrent <i>insert</i>, which a
/// row-level concurrency token cannot help with (there is no existing row to version yet); it is
/// guarded instead by the unique index (real database) plus an in-process keyed lock (this
/// environment's InMemory provider, which does not enforce unique indexes at all - see
/// <c>EfIdempotencyStore</c>'s remarks). After that first insert, the only writer of a given row for
/// the rest of its life is the single request that reserved it, so db-conventions.md §4's own
/// criterion for adding a RowVersion ("mutated by more than one role/user... a realistic concurrent-
/// edit window") is not met here.</para>
/// </summary>
public sealed class IdempotencyKey : Entity, ITenantOwned
{
    /// <summary>Matches <c>IdempotencyKeyConfiguration</c>'s <c>HasMaxLength</c> and
    /// <c>IdempotencyMiddleware</c>'s own early-rejection check - one literal, referenced from both,
    /// rather than three independently-maintained "200"s.</summary>
    public const int MaxKeyLength = 200;

    private const int MaxRequestPathLength = 500;

    /// <summary>Hex-encoded SHA-256 is always exactly 64 characters.</summary>
    private const int RequestHashLength = 64;

    public Guid TenantId { get; private set; }

    /// <summary>The raw <c>Idempotency-Key</c> header value, verbatim - client-minted (this
    /// codebase's outbox mints a UUID per queued item; see <c>generateOutboxId</c>), never
    /// server-generated.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Diagnostic-only echo of the request that first reserved this key - not itself part of
    /// the uniqueness key (that is <see cref="TenantId"/>/<see cref="Key"/> alone) but folded into
    /// <see cref="RequestHash"/> so a key accidentally reused across two different endpoints is
    /// treated as a payload mismatch rather than silently colliding.</summary>
    public string RequestMethod { get; private set; } = string.Empty;

    public string RequestPath { get; private set; } = string.Empty;

    /// <summary>What "the same payload" means - see <c>IdempotencyFingerprint</c>'s remarks for the
    /// exact algorithm (full-body hash for JSON, a metadata-only fingerprint deliberately excluding
    /// file bytes for multipart uploads). Hex-encoded SHA-256, always exactly 64 characters.</summary>
    public string RequestHash { get; private set; } = string.Empty;

    public IdempotencyRequestStatus Status { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    /// <summary>Set once, by <see cref="Complete"/>. <see langword="null"/> while
    /// <see cref="IdempotencyRequestStatus.InProgress"/>.</summary>
    public int? ResponseStatusCode { get; private set; }

    public string? ResponseContentType { get; private set; }

    /// <summary>The captured response body, UTF-8 text (every write endpoint this middleware wraps
    /// returns a small JSON DTO - see <c>IdempotencyMiddleware</c>'s class remarks on why raw photo
    /// bytes never reach here). <see langword="null"/> when <see cref="ResponseNotReplayable"/> is
    /// <see langword="true"/> - a defensive cap, not a path any of today's four wrapped endpoints can
    /// actually hit (design note: bounded, never a multi-megabyte photo response).</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>
    /// <see langword="true"/> only if the captured response body exceeded the configured replay
    /// size cap (S13-BE-01 design note: "bounded - do not store megabyte photo responses"). The
    /// ORIGINAL caller still receives their full real response regardless - this flag only affects
    /// what a future replay of the same key gets: an honest 409 rather than either silently
    /// re-running the handler (the exact duplicate this feature exists to prevent) or serving a
    /// truncated/empty body.
    /// </summary>
    public bool ResponseNotReplayable { get; private set; }

    public DateTimeOffset ReservedAt { get; private set; }

    /// <summary>Set once, by <see cref="Complete"/>. <see langword="null"/> while
    /// <see cref="IdempotencyRequestStatus.InProgress"/>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private IdempotencyKey()
    {
    }

    public IdempotencyKey(
        Guid tenantId,
        string key,
        string requestMethod,
        string requestPath,
        string requestHash,
        Guid requestedByUserId,
        DateTimeOffset reservedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("IdempotencyKey.TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DomainException("IdempotencyKey.Key is required.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new DomainException($"IdempotencyKey.Key cannot exceed {MaxKeyLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(requestMethod))
        {
            throw new DomainException("IdempotencyKey.RequestMethod is required.");
        }

        if (string.IsNullOrWhiteSpace(requestPath) || requestPath.Length > MaxRequestPathLength)
        {
            throw new DomainException($"IdempotencyKey.RequestPath is required and cannot exceed {MaxRequestPathLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != RequestHashLength)
        {
            throw new DomainException($"IdempotencyKey.RequestHash must be a {RequestHashLength}-character hex-encoded SHA-256 digest.");
        }

        if (requestedByUserId == Guid.Empty)
        {
            // Fail closed on a null actor (this task's brief; the same discipline every other
            // site-module write handler in this codebase already applies).
            throw new DomainException("IdempotencyKey.RequestedByUserId is required.");
        }

        TenantId = tenantId;
        Key = key;
        RequestMethod = requestMethod;
        RequestPath = requestPath;
        RequestHash = requestHash;
        RequestedByUserId = requestedByUserId;
        ReservedAt = reservedAt;
        Status = IdempotencyRequestStatus.InProgress;
    }

    /// <summary>
    /// The only writer of the response snapshot - called exactly once, by the same request that
    /// reserved this row, after its wrapped handler has run to a non-5xx conclusion. Throws if this
    /// row is not <see cref="IdempotencyRequestStatus.InProgress"/> (a bug: <c>EfIdempotencyStore</c>
    /// never re-fetches an already-<see cref="IdempotencyRequestStatus.Completed"/> row for
    /// completion, only for replay).
    /// </summary>
    public void Complete(int responseStatusCode, string? responseContentType, string? responseBody, bool responseNotReplayable, DateTimeOffset completedAt)
    {
        if (Status != IdempotencyRequestStatus.InProgress)
        {
            throw new DomainException(
                $"IdempotencyKey '{Id}' has already reached a terminal state ({Status}) and cannot be completed again.");
        }

        if (responseNotReplayable && responseBody is not null)
        {
            throw new DomainException("IdempotencyKey.ResponseBody must be null when ResponseNotReplayable is true.");
        }

        Status = IdempotencyRequestStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseContentType = responseContentType;
        ResponseBody = responseBody;
        ResponseNotReplayable = responseNotReplayable;
        CompletedAt = completedAt;
    }
}
