import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  createIndexedDbOutboxStorage,
  createOutboxStore,
  createSyncEngine,
  detectSyncCapability,
  getCurrentOutboxOwner,
} from './outbox'
import type { OutboxItem, SyncCapability } from './outbox'
import { ALL_OUTBOX_KINDS, buildSiteOutboxUploaders } from './siteOutboxRegistry'

/** How often the badge re-reads counts from IndexedDB while mounted, beyond the event-driven
 * triggers below — a device-shared tablet's Sidebar realistically stays mounted for a whole shift,
 * so *some* passive refresh is needed to reflect a feature page's own background sync (S13-FE-01's
 * per-page hooks) without this hook needing a cross-instance pub/sub mechanism this codebase does not
 * have. Deliberately not shorter: this is a status *display*, not a correctness-critical read — the
 * event-driven triggers (`online`, `visibilitychange`, every local mutation) cover the moments that
 * actually matter, and this interval only needs to catch up eventually. */
const POLL_INTERVAL_MS = 5_000

export interface OutboxSyncStatus {
  /** Every not-yet-purged item owned by the current session, across every `kind`, oldest first. */
  items: OutboxItem[]
  /** `queued | syncing | failed` — not yet resolved either way. */
  pendingCount: number
  /** `failed` only — a transient error, auto-retried on the next trigger. */
  failedCount: number
  /** `conflict` only — terminal; see `services/outbox/errors.ts#OutboxConflictError`. Never
   * auto-retried, so this needs to stay visible until a human acts on it. */
  conflictCount: number
  /** Not-yet-synced items that still hold a `Blob` (only `kind: 'photo'` ever does) — the precise
   * set the sign-out gate's blob-loss warning (N-03, Sprint 12 security review) is about; a pending
   * weather-log/progress-batch item has no bytes for `authLifecycle.ts`'s logout quarantine to drop,
   * so folding it into this same count would overstate what signing out actually destroys. */
  pendingBlobCount: number
  syncCapability: SyncCapability
  isSyncing: boolean
  /** Flushes every registered kind (`siteOutboxRegistry.ts`) once, then reloads. Never throws (a
   * per-item failure is recorded on that item, not raised here) — safe to call from a plain
   * `onClick`. */
  syncNow: () => Promise<void>
  reload: () => Promise<void>
}

/**
 * S13-FE-03: system-wide sync status — pending/failed/conflict counts and a manual "sync now" that
 * works from *any* screen, backing `SyncStatusBadge.tsx` and the Sidebar's sign-out gate (N-03).
 * Owner-scoped exactly like every per-feature outbox hook (`getCurrentOutboxOwner`) — signing in as a
 * different user on a shared device shows *that* user's own counts, never the previous session's.
 */
export function useOutboxSyncStatus(): OutboxSyncStatus {
  const [items, setItems] = useState<OutboxItem[]>([])
  const [syncCapability, setSyncCapability] = useState<SyncCapability>('fallback-only')
  const [isSyncing, setIsSyncing] = useState(false)

  const storageAdapter = useMemo(() => createIndexedDbOutboxStorage(), [])
  const store = useMemo(
    () => createOutboxStore({ storage: storageAdapter, getOwner: getCurrentOutboxOwner }),
    [storageAdapter],
  )
  const syncEngine = useMemo(
    () => createSyncEngine({ store, uploaders: buildSiteOutboxUploaders(store) }),
    [store],
  )

  const reload = useCallback(async () => {
    try {
      setItems(await store.list())
    } catch {
      // Best-effort status display, mounted on *every* screen (`Sidebar.tsx`) — a storage failure
      // (e.g. IndexedDB genuinely unavailable: locked-down browser mode, quota/policy restrictions)
      // must degrade to "nothing known" rather than take an unhandled rejection down through the app
      // shell. The per-feature outbox hooks (`usePhotoOutbox.ts` etc.) still surface the same failure
      // loudly to the one screen actually trying to write at the time.
      setItems([])
    }
  }, [store])

  const syncNow = useCallback(async () => {
    setIsSyncing(true)
    try {
      await syncEngine.flush()
    } catch {
      // Honours this hook's own documented "never throws" contract — a per-item failure is already
      // recorded on that item by `syncEngine.ts` itself; this only guards the (storage-level, not
      // per-item) case where `flush()`'s own `store.pending()` read fails outright.
    } finally {
      setIsSyncing(false)
    }
    await reload()
  }, [syncEngine, reload])

  const reloadRef = useRef(reload)
  useEffect(() => {
    reloadRef.current = reload
  })

  useEffect(() => {
    let unmounted = false

    void (async () => {
      try {
        // Every kind, explicitly — not automatic from constructing one shared `syncEngine` (S13-FE-01
        // task note). Each per-feature hook also does this for its own kind(s) when *that* page
        // mounts; doing it again here is a safe no-op the second time (nothing left `syncing` to
        // revive) and is what makes a stranded item visible here even if its own feature page is
        // never revisited this session.
        await Promise.all(ALL_OUTBOX_KINDS.map((kind) => store.reconcileInterruptedSyncs(kind)))
      } catch {
        // Same degrade-not-crash reasoning as `reload`'s own catch — this hook mounts on every
        // screen via `Sidebar.tsx`.
      }
      if (unmounted) return
      void reloadRef.current()
      setSyncCapability(detectSyncCapability())
    })()

    // Read-only re-checks — deliberately *not* an auto-flush (see this hook's own remarks on why two
    // independent engines both auto-flushing on every reconnect would be wasteful, even though it is
    // now safe): the badge is a status display everywhere except its own explicit "sync now" button;
    // each feature page's own hook already owns the auto-sync-on-reconnect behaviour for its kind(s).
    const handleRecheck = () => void reloadRef.current()
    window.addEventListener('online', handleRecheck)
    document.addEventListener('visibilitychange', handleRecheck)
    const interval = window.setInterval(handleRecheck, POLL_INTERVAL_MS)

    return () => {
      unmounted = true
      window.removeEventListener('online', handleRecheck)
      document.removeEventListener('visibilitychange', handleRecheck)
      window.clearInterval(interval)
    }
  }, [store])

  const pendingCount = items.filter((item) => item.status === 'queued' || item.status === 'syncing' || item.status === 'failed').length
  const failedCount = items.filter((item) => item.status === 'failed').length
  const conflictCount = items.filter((item) => item.status === 'conflict').length
  const pendingBlobCount = items.filter((item) => item.status !== 'synced' && item.blob !== null).length

  return {
    items,
    pendingCount,
    failedCount,
    conflictCount,
    pendingBlobCount,
    syncCapability,
    isSyncing,
    syncNow,
    reload,
  }
}
