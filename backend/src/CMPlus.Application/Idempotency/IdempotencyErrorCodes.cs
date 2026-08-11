namespace CMPlus.Application.Idempotency;

/// <summary>Stable error codes <c>IdempotencyMiddleware</c> maps through <c>ResultProblemMapper</c>
/// (S2-BE-03's convention) - even though the middleware is not a MediatR handler and never produces a
/// <c>Result</c>, reusing the same code-&gt;ProblemDetails table keeps every error this API returns
/// shaped identically rather than growing a second, parallel error format.</summary>
public static class IdempotencyErrorCodes
{
    /// <summary>The same key was already used - either it is still being processed
    /// (<c>InProgressElsewhere</c>-shaped 409, see the sibling code below) or it already completed -
    /// with a request that hashes differently (different body, different multipart file/fields, or a
    /// different method/route entirely). The caller must mint a new key for a genuinely different
    /// operation; the DoD's own wording ("key เดิมกับ payload ต่างกัน → 409").</summary>
    public const string PayloadMismatch = "IdempotencyPayloadMismatch";

    /// <summary>A request with this exact key is already in flight (this process, or - on a real
    /// database - a concurrent request that lost the race to insert the reservation row). Ask the
    /// client to retry shortly; the wrapped handler is never invoked for this request.</summary>
    public const string RequestInProgress = "IdempotencyRequestInProgress";

    /// <summary>Fail-closed guard (this task's brief: "fail closed on a null actor") - every existing
    /// site-module write handler in this codebase takes the identical stance on a request that
    /// reaches it with no resolvable authenticated user.</summary>
    public const string ActorRequired = "IdempotencyActorRequired";

    /// <summary><c>Idempotency-Key</c> was supplied but is empty, whitespace-only, longer than
    /// <see cref="CMPlus.Domain.Entities.IdempotencyKey.MaxKeyLength"/>, or repeated more than once
    /// on the same request.</summary>
    public const string KeyInvalid = "IdempotencyKeyInvalid";

    /// <summary>The completed response this key maps to exceeded the configured replay size cap and
    /// was never stored (S13-BE-01 design note: "bounded - do not store megabyte photo responses").
    /// Not reachable by any of today's four wrapped endpoints, all of which return a small JSON DTO -
    /// see <c>IdempotencyMiddleware</c>'s class remarks.</summary>
    public const string ResponseNotReplayable = "IdempotencyResponseNotReplayable";
}
