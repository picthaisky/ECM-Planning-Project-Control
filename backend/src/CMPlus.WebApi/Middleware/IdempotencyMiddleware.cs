using System.Text;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Idempotency;
using CMPlus.Domain.Entities;
using CMPlus.WebApi.ErrorHandling;
using Microsoft.Extensions.Primitives;

namespace CMPlus.WebApi.Middleware;

/// <summary>
/// S13-BE-01 (US-13.1, ADR-0005): <c>Idempotency-Key</c> support on the site-module write endpoints,
/// closing security review sprint-12.md M-01 - the offline outbox's <c>reconcileInterruptedSyncs</c>
/// can replay an upload whose response the client never saw, and until this landed nothing
/// server-side honoured the key it already sent, so the replay created a second row.
///
/// <para><b>Scope - which endpoints carry <c><see cref="IdempotentAttribute"/></c>, and why exactly
/// these.</b> The DoD says "the site-module write endpoints"; this wraps precisely where
/// <c>web/src/services/outbox</c> actually posts, not every mutating endpoint in the API:
/// <c>POST /api/v1/projects/{id}/photos</c> (the outbox's one kind today, S12-FE-01),
/// <c>POST /api/v1/projects/{id}/weather-logs</c> and its <c>/corrections</c> sibling, and
/// <c>POST /api/v1/projects/{id}/progress/batch</c> (the two kinds S13-FE-01 adds this same sprint,
/// per <c>docs/10.</c>'s own dependency line for that task). Man/Equipment (S12-BE-02) is
/// deliberately NOT wrapped - ADR-0005's own context names only "photo/weather/progress capture" as
/// the offline-first site modules, and S12-FE-02 (the Man/Equipment screen) carries no offline/outbox
/// DoD at all, unlike S12-FE-01/S13-FE-01. Applying this blanket-wide "for safety" would be exactly
/// the "blanket-wrapping every mutation" this task's brief warns against - an endpoint nothing ever
/// retries gains a table write and a request-body hash for no correctness benefit.</para>
///
/// <para><b>Payload identity - what "the same payload" means.</b> See
/// <see cref="IdempotencyFingerprint"/> for the full reasoning; in short, a JSON body is hashed whole
/// (cheap - these bodies are small), while a multipart body (photo upload) is fingerprinted by shape
/// (field values, file name/Content-Type/length) and never by its file bytes, because re-hashing a
/// multi-megabyte photo on every request - including every replay, the exact case this feature exists
/// to make cheap - would be real, avoidable cost.</para>
///
/// <para><b>Concurrency.</b> Two simultaneous requests for the same key must not both reach the
/// wrapped handler. The database's own unique <c>(TenantId, Key)</c> index (S13-DB-01 migration) is
/// the cross-instance guarantee; this environment cannot prove it fires at all (EF Core InMemory - no
/// SQL Server, Docker cannot start here - enforces no unique indexes), so the loser is additionally,
/// and verifiably in this environment, made to wait on a real in-process lock and then 409 rather than
/// duplicate - see <c>IIdempotencyKeyLock</c>/<c>EfIdempotencyStore</c>'s remarks for exactly what is
/// and is not proven here.</para>
///
/// <para><b>What is stored, and why it is never the created resource's raw bytes.</b> Status, Content-
/// Type and body, bounded (<see cref="IIdempotencyOptionsProvider.MaxReplayableResponseBodyBytes"/>).
/// This does not need a separate "store the created resource's representation instead of raw bytes"
/// rule bolted on: every endpoint this middleware wraps already returns the created/updated resource's
/// small JSON DTO as its response body (<c>PhotoDto</c>/<c>WeatherLogDto</c>/
/// <c>BatchRecordProgressResultDto</c>) - raw photo bytes are only ever returned by the separate
/// <c>GET .../photos/{id}</c> action, which is naturally idempotent already (no side effect) and is
/// not, and does not need to be, wrapped by this middleware at all. Capturing the real response IS
/// capturing the resource's representation, by construction, for every endpoint in scope.</para>
///
/// <para><b>A replay never re-runs side effects.</b> On <see cref="IdempotencyReservationOutcome.AlreadyCompleted"/>
/// the wrapped handler is never invoked - no second <c>Photo</c>/<c>DailyWeatherLog</c>/
/// <c>ActivityProgressLog</c> row, no second business-entity <c>AuditLog</c> row (the interceptor only
/// ever sees a change tracker with something in it when the handler actually runs), no second ledger
/// row. This request also never touches the <c>IdempotencyKey</c> row itself (a pure read), so it adds
/// no bookkeeping <c>AuditLog</c> row either - only the original request's Reserve/Complete pair does,
/// correctly attributed to the original actor.</para>
///
/// <para><b>Fails closed on a null actor</b> (this task's brief), mirroring every existing site-module
/// write handler in this codebase (<c>UploadPhotoCommandHandler</c>, <c>RecordWeatherLogCommandHandler</c>,
/// etc.) rather than fabricating <see cref="Guid.Empty"/>.</para>
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(
        HttpContext context,
        IIdempotencyStore store,
        IIdempotencyOptionsProvider options,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        IDateTimeProvider clock)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null)
        {
            await next(context);
            return;
        }

        if (!TryGetKey(context.Request.Headers, out var key, out var keyError))
        {
            if (keyError is not null)
            {
                await WriteProblemAsync(context, keyError);
                return;
            }

            // No Idempotency-Key supplied at all - this task's brief and the DoD's own wording
            // ("รองรับ" - support, not require) make the header opt-in, not mandatory.
            await next(context);
            return;
        }

        // Fail closed on a null actor - checked before any DB or body work, mirroring
        // UploadPhotoCommandHandler/RecordWeatherLogCommandHandler's identical "is not { }" guard.
        if (currentUser.UserId is not { } actorUserId)
        {
            await WriteProblemAsync(context, IdempotencyErrorCodes.ActorRequired);
            return;
        }

        var tenantId = tenantProvider.TenantId;
        var requestHash = await IdempotencyFingerprint.ComputeAsync(context.Request, context.RequestAborted);
        var now = clock.UtcNow;

        var reservation = await store.ReserveAsync(
            tenantId, key, context.Request.Method, context.Request.Path.Value ?? string.Empty,
            requestHash, actorUserId, now, context.RequestAborted);

        switch (reservation.Outcome)
        {
            case IdempotencyReservationOutcome.PayloadMismatch:
                await WriteProblemAsync(context, IdempotencyErrorCodes.PayloadMismatch);
                return;

            case IdempotencyReservationOutcome.InProgressElsewhere:
                await WriteProblemAsync(context, IdempotencyErrorCodes.RequestInProgress);
                return;

            case IdempotencyReservationOutcome.AlreadyCompleted:
                await ReplayAsync(context, reservation);
                return;

            case IdempotencyReservationOutcome.Reserved:
                await ExecuteAndCaptureAsync(context, store, reservation.IdempotencyKeyId!.Value, options, clock);
                return;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(IdempotencyReservationOutcome)} '{reservation.Outcome}'.");
        }
    }

    /// <summary>Header parsing/validation only - never touches the store. A key repeated more than
    /// once, or present-but-empty/oversized, is rejected outright rather than silently taking the
    /// first value or silently treating it as "no key supplied".</summary>
    private static bool TryGetKey(IHeaderDictionary headers, out string key, out string? error)
    {
        key = string.Empty;
        error = null;

        if (!headers.TryGetValue(HeaderName, out StringValues values) || values.Count == 0)
        {
            return false;
        }

        if (values.Count > 1)
        {
            error = IdempotencyErrorCodes.KeyInvalid;
            return true;
        }

        var candidate = values[0]?.Trim() ?? string.Empty;
        if (candidate.Length == 0 || candidate.Length > IdempotencyKey.MaxKeyLength)
        {
            error = IdempotencyErrorCodes.KeyInvalid;
            return true;
        }

        key = candidate;
        return true;
    }

    /// <summary>
    /// Runs the wrapped handler with <see cref="HttpContext.Response"/>'s body redirected into a
    /// buffer so the final bytes can be both recorded (for a future replay) and still delivered to
    /// this caller unchanged - the standard "response-capturing middleware" shape. Must be registered
    /// after <c>UseResponseCompression()</c> (see <c>Program.cs</c>) so compression sees this method's
    /// final, restored plain bytes and compresses them exactly as it would if this middleware were not
    /// present, rather than compressing into (or being bypassed by) this buffer.
    /// </summary>
    // Not static: needs the primary constructor's `next` (RequestDelegate), which C# only allows an
    // instance member to capture.
    private async Task ExecuteAndCaptureAsync(
        HttpContext context, IIdempotencyStore store, Guid idempotencyKeyId, IIdempotencyOptionsProvider options, IDateTimeProvider clock)
    {
        var originalBody = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;

        var handlerThrew = false;
        try
        {
            await next(context);
        }
        catch
        {
            handlerThrew = true;
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;

            if (handlerThrew)
            {
                // Never memoize an exception - GlobalExceptionHandler (registered above this
                // middleware in Program.cs's pipeline) writes the real 500 to originalBody once this
                // finally block unwinds; delete the reservation so the very next retry with the same
                // key can attempt the real operation again rather than being permanently blocked.
                // CancellationToken.None deliberately, not context.RequestAborted: this cleanup must
                // still happen even if the exception WAS a client disconnect.
                await store.ReleaseAsync(idempotencyKeyId, CancellationToken.None);
            }
            else if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                // The handler returned normally but with an unexpected server-error status - same
                // treatment as the throw case, and for the same reason (a transient/infrastructure
                // failure must remain retryable, never cached).
                await store.ReleaseAsync(idempotencyKeyId, CancellationToken.None);
                await originalBody.WriteAsync(captured.ToArray(), context.RequestAborted);
            }
            else
            {
                var bodyBytes = captured.ToArray();
                var notReplayable = bodyBytes.Length > options.MaxReplayableResponseBodyBytes;
                var bodyText = notReplayable ? null : Encoding.UTF8.GetString(bodyBytes);

                // CancellationToken.None deliberately: by this point the wrapped handler has already
                // fully succeeded (its own SaveChanges already committed, e.g. the Photo row exists) -
                // this call is what makes that success durably replayable. Skipping it because the
                // client happened to disconnect in the last few milliseconds would leave the
                // reservation stuck InProgress and let a later retry re-run an already-successful
                // operation a second time, i.e. resurrect the exact M-01 bug this feature closes.
                await store.CompleteAsync(
                    idempotencyKeyId, context.Response.StatusCode, context.Response.ContentType, bodyText, notReplayable,
                    clock.UtcNow, CancellationToken.None);

                await originalBody.WriteAsync(bodyBytes, context.RequestAborted);
            }
        }
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyReservation reservation)
    {
        if (reservation.ResponseNotReplayable || reservation.ResponseBody is null)
        {
            // Defensive, not reachable by any of today's four wrapped endpoints (see this type's
            // class remarks) - fails closed rather than either re-running the handler (the duplicate
            // this feature exists to prevent) or serving an empty/truncated body silently.
            await WriteProblemAsync(context, IdempotencyErrorCodes.ResponseNotReplayable);
            return;
        }

        context.Response.StatusCode = reservation.ResponseStatusCode!.Value;
        if (reservation.ResponseContentType is not null)
        {
            context.Response.ContentType = reservation.ResponseContentType;
        }

        await context.Response.WriteAsync(reservation.ResponseBody, context.RequestAborted);
    }

    private static Task WriteProblemAsync(HttpContext context, string errorCode)
    {
        var problem = ResultProblemMapper.ToProblemDetails(errorCode, context.Request.Path);
        context.Response.StatusCode = problem.Status!.Value;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
