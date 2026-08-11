import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  createIndexedDbOutboxStorage,
  createOutboxStore,
  createSyncEngine,
  detectSyncCapability,
  getCurrentOutboxOwner,
  registerOutboxSyncTriggers,
} from '../../services/outbox'
import type { OutboxItem, OutboxSyncTriggersHandle, OutboxUploader, SyncCapability } from '../../services/outbox'
import { compressPhotoFile } from './compression'
import { PhotoApiError } from './api'
import { PHOTO_OUTBOX_KIND, uploadPhotoOutboxItem } from './photoOutbox'
import type { PhotoOutboxPayload } from './photoOutbox'
import type { UploadPhotoFields } from './types'

export type PhotoOutboxProcessingState = 'idle' | 'compressing' | 'error'

/**
 * Drives S12-FE-01's capture -> outbox -> sync flow end to end: compresses+bakes-orientation
 * (`compression.ts`), enqueues into the generic outbox (`services/outbox/`), and wires the fallback
 * sync triggers (`syncTriggers.ts`) so a flush actually happens without relying on Background Sync
 * (this sprint ships no service worker at all — see `syncTriggers.ts`'s own remarks).
 *
 * One `OutboxStorageAdapter`/`OutboxStore`/`SyncEngine` triple is created once per `projectId` (via
 * `useMemo`) and lives for the component's lifetime — re-created only if the user switches projects.
 */
export function usePhotoOutbox(projectId: string) {
  const [items, setItems] = useState<OutboxItem<PhotoOutboxPayload>[]>([])
  const [processingState, setProcessingState] = useState<PhotoOutboxProcessingState>('idle')
  const [processingError, setProcessingError] = useState<string | null>(null)
  const [syncCapability, setSyncCapability] = useState<SyncCapability>('fallback-only')

  const storageAdapter = useMemo(() => createIndexedDbOutboxStorage(), [])
  // `getOwner` is passed by reference, not invoked here — `outboxStore.ts` calls it fresh on every
  // operation (never memoizes the result), which is what makes the H-02 ownership fix hold even if
  // this `usePhotoOutbox` instance somehow outlived a login/logout switch (see that file's own
  // remarks). Re-created only if the storage adapter itself changes, matching `store`'s own lifetime.
  const store = useMemo(
    () => createOutboxStore({ storage: storageAdapter, getOwner: getCurrentOutboxOwner }),
    [storageAdapter],
  )
  const syncEngine = useMemo(
    () =>
      createSyncEngine({
        store,
        // The registry is necessarily kind-agnostic (`Record<string, OutboxUploader>`) while this
        // single registration is kind-specific (`OutboxUploader<PhotoOutboxPayload>`) — the cast is
        // the map boundary itself, not a workaround for an unsound call; `uploadPhotoOutboxItem` is
        // only ever invoked here for items already filtered to `kind: PHOTO_OUTBOX_KIND`.
        uploaders: { [PHOTO_OUTBOX_KIND]: uploadPhotoOutboxItem as OutboxUploader },
      }),
    [store],
  )

  const reload = useCallback(async () => {
    const loaded = (await store.list(PHOTO_OUTBOX_KIND)) as OutboxItem<PhotoOutboxPayload>[]
    // L-06 (Sprint 12 security review): `store.list` is already scoped to the current owner, but a
    // single owner can still hold queued items from a *different* project (they switched projects
    // mid-shift without syncing) — this screen is project-scoped (`/app/:projectId/photo`) and must
    // only ever show/report on *this* project's items, not the owner's whole device-wide queue.
    setItems(loaded.filter((item) => item.payload.projectId === projectId))
  }, [store, projectId])

  // Intentionally syncs the owner's *whole* pending set, not only this screen's `projectId` — unlike
  // `reload`'s display-only filter (L-06) above, gating an actual upload attempt on which project
  // screen happens to be open would leave another project's queued photos stuck pending forever
  // whenever the user is not looking at that specific project's Photo Progress page.
  const syncNow = useCallback(async () => {
    await syncEngine.flush()
    await reload()
  }, [syncEngine, reload])

  // Keeps the *current* syncNow/reload reachable from the sync-trigger listeners without making
  // them a dependency of the registration effect below — mirrors `components/Modal.tsx#onCloseRef`'s
  // established reasoning: `syncNow` is a fresh closure every render (it closes over `syncEngine`),
  // and re-registering `window`/`document` listeners on every render would be wasteful and, worse,
  // would create a window where a rapid double-mount briefly has zero listeners attached.
  const syncNowRef = useRef(syncNow)
  useEffect(() => {
    syncNowRef.current = syncNow
  })

  useEffect(() => {
    let unmounted = false
    let handle: OutboxSyncTriggersHandle | null = null

    void (async () => {
      // Reconcile any item this device's *previous* session left stuck at `syncing` (tab/app killed,
      // phone locked, page reloaded mid-upload — see `outboxStore.ts#reconcileInterruptedSyncs`'s own
      // remarks) back to a retryable state, and — critically — await it before `reload()`/registering
      // the sync triggers below, so the very first auto-sync attempt this mount makes (the
      // `navigator.onLine` check inside `registerOutboxSyncTriggers`) already sees the reconciled item
      // as pending, rather than racing its own write.
      await store.reconcileInterruptedSyncs(PHOTO_OUTBOX_KIND)
      if (unmounted) return

      void reload()
      setSyncCapability(detectSyncCapability())

      handle = registerOutboxSyncTriggers(() => {
        void syncNowRef.current()
      })
    })()

    // Bounded retention for `synced` records (H-02 fix bullet 3) is swept once at app boot
    // (`main.tsx`), device-wide and owner-agnostic — deliberately *not* re-run per feature-hook
    // mount, so S13-FE-01's weather-log/progress-batch hooks need no equivalent of their own.

    return () => {
      unmounted = true
      handle?.dispose()
    }
  }, [reload, store])

  const capture = useCallback(
    async (file: File, fields: UploadPhotoFields) => {
      setProcessingState('compressing')
      setProcessingError(null)
      try {
        const compressed = await compressPhotoFile(file)
        await store.enqueue<PhotoOutboxPayload>({
          kind: PHOTO_OUTBOX_KIND,
          payload: { projectId, fields, fileName: file.name || 'photo.jpg' },
          blob: compressed.blob,
          blobFileName: file.name || 'photo.jpg',
          blobContentType: compressed.blob.type,
        })
        await reload()
        setProcessingState('idle')
        // Take a shot at syncing right away, but **only** when the browser itself reports being
        // online — this is exactly the DoD's offline case ("ตัดเน็ตแล้วถ่ายรูป → เข้า IndexedDB outbox
        // แสดง 'รอซิงค์'"), and unconditionally attempting a flush here would race a genuinely offline
        // capture against a network call that has no business being attempted yet, marking a
        // never-actually-tried item `failed` before the item even has a real network history — a real
        // bug caught by `usePhotoOutbox.test.ts`'s own offline-then-reconnect fixture, not a
        // hypothetical. `navigator.onLine` false-positives (rare, and never false-negative) are an
        // accepted imprecision here since the fallback triggers (`syncTriggers.ts`) still cover them.
        if (typeof navigator !== 'undefined' && navigator.onLine) {
          void syncNow()
        }
        return true
      } catch (error) {
        setProcessingState('error')
        setProcessingError(error instanceof PhotoApiError ? error.message : 'ประมวลผล/บันทึกรูปภาพไม่สำเร็จ กรุณาลองใหม่')
        return false
      }
    },
    [projectId, store, reload, syncNow],
  )

  // There is deliberately no per-item `retry(id)` — `syncEngine.ts#flush` always drains the *whole*
  // pending set (`queued` and `failed` items alike) in one pass, so "retry this one failed photo"
  // and "sync now" are the same operation; a per-item variant would need to filter the pending set
  // by id, which buys nothing (the other pending items would still need to go out eventually, and
  // doing them together is strictly fewer round trips on a connection worth conserving). Each failed
  // row's "ลองใหม่" button calls `syncNow` directly (`PhotoOutboxList.tsx`).

  return { items, processingState, processingError, syncCapability, capture, syncNow, reload }
}
