/**
 * S13-FE-01: cross-cutting error types for the outbox's idempotency contract (S13-BE-01's
 * `IdempotencyMiddleware`). Kind-agnostic — every uploader (`photoOutbox.ts`, `weatherOutbox.ts`,
 * `progressOutbox.ts`) shares these rather than each re-inventing the 409 split below.
 */

/**
 * Thrown by an `OutboxUploader` when the server's response means "this queued item, sent again with
 * its unmodified payload, will never succeed" — as opposed to an ordinary thrown `Error`, which
 * `syncEngine.ts#flush` treats as transiently retryable (back to `failed`, picked up by the next
 * `pending()` sweep). `syncEngine.ts` special-cases this type and routes the item to the terminal
 * `'conflict'` status instead: retrying forever with the same key+payload against
 * `IdempotencyPayloadMismatch` is provably futile (the server already told us the payload disagrees
 * with what it accepted under this key) and would just burn battery/bandwidth on a site device while
 * showing the user a permanently-spinning "failed, retrying" state that can never resolve itself.
 *
 * The item's `lastError` still carries a Thai, human-legible explanation (S13-FE-03 DoD) — this is
 * not a silent drop, it is a *different terminal state* the sync-status UI renders distinctly from
 * an ordinary retryable failure (see `SyncStatusBadge.tsx`).
 */
export class OutboxConflictError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'OutboxConflictError'
  }
}

/**
 * Raw `ProblemDetails.detail` values (`IdempotencyErrorCodes`, `backend/src/CMPlus.Application/Idempotency/IdempotencyErrorCodes.cs`)
 * that mean "blind retry with the same payload will never succeed" — as opposed to
 * `IdempotencyRequestInProgress`, a transient in-flight collision (e.g. the sync-status badge's own
 * manual retry racing a feature page's auto-sync-on-reconnect) that ordinary retry resolves on its
 * own once the in-flight request finishes, and which is deliberately *not* in this set so it stays in
 * the normal retryable `failed` bucket.
 *
 * Each `features/*\/api.ts`'s `Xxx­ApiError` exposes the raw backend code as `.code` (alongside the
 * already-Thai `.message`) specifically so an uploader can check it against this set without string-
 * matching a translated message.
 */
export const IDEMPOTENCY_CONFLICT_CODES: ReadonlySet<string> = new Set([
  'IdempotencyPayloadMismatch',
  'IdempotencyResponseNotReplayable',
])
