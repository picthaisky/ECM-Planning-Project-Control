import type { OutboxStorageAdapter } from './storage'
import type { OutboxOwner } from './types'

/**
 * Device-wide outbox housekeeping (Sprint 12 security review, H-02 fix bullets 2 and 3). Both
 * functions here operate directly against `OutboxStorageAdapter` — not through a single kind's
 * `OutboxStore` instance — because they must reach records belonging to *any* owner and *any* `kind`
 * (photos today; S13-FE-01's weather-log/progress-batch items tomorrow, with zero changes to this
 * file), independent of which feature hook happens to be mounted when they run.
 */

/**
 * Logout-time privacy quarantine (H-02 fix bullet 2): drops the raw attachment bytes (`blob`) from
 * every outbox record owned by the signing-out session, across every `kind`. Deliberately does NOT
 * delete the record itself (caption/fields/status/attemptCount survive) so:
 *
 *   - a `queued`/`failed` item that has not synced yet fails loudly and specifically on its *next*
 *     upload attempt (the existing "ไม่พบไฟล์รูปภาพของรายการนี้ในเครื่อง ... กรุณาถ่ายรูปนี้ใหม่" path
 *     — `features/photo/photoOutbox.ts#uploadPhotoOutboxItem`) instead of silently vanishing; if the
 *     same person logs back in, they see exactly what needs to be recaptured, not an empty queue;
 *   - a `synced` item already has `blob: null` (`outboxStore.ts#markSynced`), so this is a no-op for
 *     it — nothing here duplicates that.
 *
 * This is independent of, and defense-in-depth alongside, `outboxStore.ts`'s owner filter on
 * `list()`/`pending()`: that filter already makes another session's items invisible through the
 * app's own UI (and un-flushable by it) the moment a different user logs in — it does not require
 * this function to run first. What this function additionally bounds is the raw bytes sitting in
 * IndexedDB *at rest*, reachable by anyone with direct access to the shared device's storage (e.g.
 * browser devtools), independent of which app session is active — at the deliberate cost of losing
 * the still-unsynced blob itself. That is a confidentiality-over-availability tradeoff, made
 * consciously for a shared construction-site device per the review; it does not touch the record's
 * metadata, so nothing here is silent.
 *
 * Returns the number of records whose blob was actually cleared (0 if the owner had none queued, or
 * everything they had was already blob-less).
 */
export async function quarantineOwnerBlobs(storage: OutboxStorageAdapter, owner: OutboxOwner): Promise<number> {
  const all = await storage.list()
  const toQuarantine = all.filter(
    (item) => item.ownerUserId === owner.userId && item.ownerTenantId === owner.tenantId && item.blob !== null,
  )

  for (const item of toQuarantine) {
    await storage.put({ ...item, blob: null })
  }

  return toQuarantine.length
}

/** Default bound on how long a `synced` record's metadata (caption, `fileName`, `capturedAt`, etc.)
 * survives after acknowledgement — 7 days. Chosen to comfortably cover "the site engineer wants to
 * glance back at what was uploaded this week" while keeping the free-text-caption PII window short;
 * tune via `purgeExpiredSyncedItems`'s own `maxAgeMs` option if product guidance says otherwise. */
export const DEFAULT_SYNCED_RETENTION_MS = 7 * 24 * 60 * 60 * 1000

export interface PurgeExpiredSyncedItemsOptions {
  /** Injectable for tests; defaults to `() => new Date().toISOString()`. */
  now?: () => string
  /** Defaults to `DEFAULT_SYNCED_RETENTION_MS`. */
  maxAgeMs?: number
  /** Restrict the sweep to one `kind`; omit to purge across every kind. */
  kind?: string
}

/**
 * Bounded retention for `synced` records (H-02 fix bullet 3). `markSynced` already drops the `blob`
 * immediately (the biggest item), but the rest of a record — including free-text fields like a photo
 * caption, exactly where a Thai site log ends up with a person's name or an injury detail (see the
 * review's own example: `"crack in beam B12, worker somchai injured here"`) — otherwise persists
 * forever with no purge.
 *
 * Deletes any `synced` record whose `syncedAt` is older than `maxAgeMs`. Deliberately **not**
 * owner-scoped: this is a device-wide housekeeping sweep, so it also reaps records left behind by
 * *previous* users, not only the currently signed-in one — scoping it to "my own items" would leave
 * every earlier user's old synced metadata sitting forever on a device they may never sign back into.
 * Safe to call unconditionally on every app mount (`usePhotoOutbox.ts` does, alongside
 * `reconcileInterruptedSyncs`) — a no-op once nothing in the window remains expired.
 *
 * Returns the number of records deleted.
 */
export async function purgeExpiredSyncedItems(
  storage: OutboxStorageAdapter,
  options: PurgeExpiredSyncedItemsOptions = {},
): Promise<number> {
  const now = options.now ?? (() => new Date().toISOString())
  const maxAgeMs = options.maxAgeMs ?? DEFAULT_SYNCED_RETENTION_MS
  const nowMs = new Date(now()).getTime()

  const all = await storage.list(options.kind)
  const expired = all.filter((item) => {
    if (item.status !== 'synced' || !item.syncedAt) return false
    const syncedMs = new Date(item.syncedAt).getTime()
    return Number.isFinite(syncedMs) && nowMs - syncedMs >= maxAgeMs
  })

  for (const item of expired) {
    await storage.delete(item.id)
  }

  return expired.length
}
