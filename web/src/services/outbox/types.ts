/**
 * Generic offline-write outbox (ADR-0005, S12-FE-01). This is deliberately **not** photo-specific —
 * `docs/10.` §8/§9 sequence S12-FE-01 first (photos only) then S13-FE-01 ("ขยาย outbox ให้ครอบ
 * Weather Log และ batch progress update") reusing the *same* `web/src/services/outbox/` module, so
 * the queue item shape here carries a `kind` discriminator and a JSON-serializable `payload` rather
 * than photo-shaped fields, and a S13 kind (weather-log/progress-batch) is a new `kind` string + a
 * new uploader registration, never a change to this file.
 *
 * **Idempotency key, from day one.** `docs/10.` S13-BE-01 is the *server* that will honour an
 * `Idempotency-Key` header, but the key itself is assigned here, at enqueue time, in S12 — not
 * deferred to S13. Retrying an upload whose response was lost on a flaky site connection is the
 * *normal* case this module exists for; a key minted only once the server understands it would leave
 * every item already sitting in a device's outbox before that day unprotected forever (the device
 * queued them before the upgrade and never re-enqueues them). Minting it now costs nothing (the
 * server ignores an unrecognised header) and closes that gap permanently.
 */

/** `queued` -> `syncing` -> `synced` (terminal) or -> `failed` (retryable, back to `queued` semantics
 * on the next flush — see `syncEngine.ts`, which treats `failed` and `queued` identically as
 * "pending") or -> `conflict` (terminal, S13-FE-01/`errors.ts#OutboxConflictError`: the server's
 * `Idempotency-Key` reservation says this exact queued payload will never succeed, so it is
 * deliberately *not* retried automatically — `PENDING_STATUSES` in `outboxStore.ts` excludes it on
 * purpose). */
export type OutboxItemStatus = 'queued' | 'syncing' | 'synced' | 'failed' | 'conflict'

/**
 * Identifies the session that owns an outbox record (Sprint 12 security review H-02). Captured once
 * at `enqueue` from the *current* authenticated session (`services/outbox/authOwner.ts`'s
 * `getCurrentOutboxOwner`, injected into `createOutboxStore` as `getOwner`) and never trusted from
 * caller-supplied payload data. `outboxStore.ts`'s `list`/`pending`/`reconcileInterruptedSyncs` all
 * refuse any record whose owner does not match the *current* session, resolved fresh on every call —
 * this is the seam that stops a shared-device queue from leaking across a logout/login boundary, and
 * it is deliberately generic (not photo-specific) so S13-FE-01's weather-log/progress-batch kinds
 * inherit it unchanged.
 */
export interface OutboxOwner {
  userId: string
  tenantId: string
}

/**
 * One queued write, of any `kind`. `TPayload` is the kind-specific JSON-serializable request fields
 * (e.g. `PhotoOutboxPayload` for `kind: 'photo'`); `blob` is the one binary attachment a write may
 * carry (a photo's compressed bytes) — `null` for kinds with no binary payload (a future weather-log
 * item, say).
 */
export interface OutboxItem<TPayload = unknown> {
  /** Client-generated primary key (never a server id — the server id, once known, lives in
   * `serverId`). Stable across retries so a re-enqueue is never mistaken for a new item. */
  id: string
  /** Discriminates which uploader (`syncEngine.ts`'s registry) handles this item and which feature
   * folder owns its payload shape. `'photo'` is the only kind S12 defines. */
  kind: string
  /** Minted once, at enqueue time, never regenerated on retry (see this file's own remarks and
   * `outboxStore.ts#enqueue`). Sent as the `Idempotency-Key` request header by the kind's uploader. */
  idempotencyKey: string
  /** The session that enqueued this item — see `OutboxOwner`'s own doc comment. Required (not
   * optional) so every new call site must supply it; a pre-fix record read from a real device's
   * IndexedDB (written before this migration) will not satisfy this shape at runtime despite the
   * static type, which is exactly why the owner-matching helpers in `outboxStore.ts` treat a
   * missing/malformed owner as "belongs to nobody" rather than trusting it optimistically. */
  ownerUserId: string
  ownerTenantId: string
  payload: TPayload
  blob: Blob | null
  /** Original file name for the blob, if any — informational only (never trusted server-side). */
  blobFileName: string | null
  blobContentType: string | null
  status: OutboxItemStatus
  /** Number of upload attempts made so far (0 before the first attempt). */
  attemptCount: number
  /** Thai-ready message from the most recent failed attempt, or `null` if it has never failed. */
  lastError: string | null
  createdAt: string
  updatedAt: string
  syncedAt: string | null
  /** The id the server assigned once synced (e.g. `PhotoDto.id`), or `null` until then. */
  serverId: string | null
}

export interface EnqueueOutboxItemInput<TPayload> {
  kind: string
  payload: TPayload
  blob?: Blob | null
  blobFileName?: string | null
  blobContentType?: string | null
}
