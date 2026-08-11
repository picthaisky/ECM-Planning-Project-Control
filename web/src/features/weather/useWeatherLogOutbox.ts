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
import {
  WEATHER_LOG_CORRECTION_OUTBOX_KIND,
  WEATHER_LOG_OUTBOX_KIND,
  createWeatherLogCorrectionUploader,
  parseWeatherLogTargetRef,
  uploadWeatherLogOutboxItem,
} from './weatherOutbox'
import type { WeatherLogCorrectionOutboxPayload, WeatherLogOutboxPayload } from './weatherOutbox'
import type { RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload } from './types'

/**
 * S13-FE-01: Weather Log's offline capture -> outbox -> sync flow, mirroring
 * `features/photo/usePhotoOutbox.ts`'s established shape closely (same storage-adapter/store/engine
 * lifecycle, same reconcile-then-register-triggers mount sequence, same "enqueue always; attempt an
 * immediate sync only when the browser already reports online" write path) but covering **two**
 * kinds — an Original entry and a Correction/Retraction — sharing one `store`/`syncEngine` pair since
 * a correction's own upload behaviour depends on the Original's state (`weatherOutbox.ts`).
 *
 * `onSynced` is called after every `syncNow()` (regardless of whether anything actually synced this
 * pass — a cheap, idempotent signal) so the caller (`WeatherPage.tsx`) can reload the server-confirmed
 * register (`useWeatherLogs.ts`) the moment a queued item lands, the same "reload the whole list on
 * any mutation" discipline `useWeatherLogActions.ts` already used before this hook replaced it.
 */
export function useWeatherLogOutbox(projectId: string, onSynced?: () => void) {
  const [items, setItems] = useState<OutboxItem[]>([])
  const [saving, setSaving] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [syncCapability, setSyncCapability] = useState<SyncCapability>('fallback-only')

  const storageAdapter = useMemo(() => createIndexedDbOutboxStorage(), [])
  const store = useMemo(
    () => createOutboxStore({ storage: storageAdapter, getOwner: getCurrentOutboxOwner }),
    [storageAdapter],
  )
  const syncEngine = useMemo(
    () =>
      createSyncEngine({
        store,
        uploaders: {
          [WEATHER_LOG_OUTBOX_KIND]: uploadWeatherLogOutboxItem as OutboxUploader,
          [WEATHER_LOG_CORRECTION_OUTBOX_KIND]: createWeatherLogCorrectionUploader(store) as OutboxUploader,
        },
      }),
    [store],
  )

  const reload = useCallback(async () => {
    const [originals, corrections] = await Promise.all([
      store.list(WEATHER_LOG_OUTBOX_KIND),
      store.list(WEATHER_LOG_CORRECTION_OUTBOX_KIND),
    ])
    // Mirrors `usePhotoOutbox.ts#reload`'s L-06 project-scoping exactly: `store.list` is already
    // owner-scoped, but a single owner can hold queued items from a different project too.
    const mine = [...originals, ...corrections]
      .filter((item) => (item.payload as { projectId?: string }).projectId === projectId)
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
    setItems(mine)
  }, [store, projectId])

  const onSyncedRef = useRef(onSynced)
  useEffect(() => {
    onSyncedRef.current = onSynced
  })

  const syncNow = useCallback(async () => {
    await syncEngine.flush()
    await reload()
    onSyncedRef.current?.()
  }, [syncEngine, reload])

  const syncNowRef = useRef(syncNow)
  useEffect(() => {
    syncNowRef.current = syncNow
  })

  useEffect(() => {
    let unmounted = false
    let handle: OutboxSyncTriggersHandle | null = null

    void (async () => {
      // Both kinds need their own reconcile call — not automatic from constructing one shared
      // `syncEngine` (see `outboxStore.ts#reconcileInterruptedSyncs`'s own remarks; each call is
      // scoped to exactly the `kind` named).
      await store.reconcileInterruptedSyncs(WEATHER_LOG_OUTBOX_KIND)
      await store.reconcileInterruptedSyncs(WEATHER_LOG_CORRECTION_OUTBOX_KIND)
      if (unmounted) return

      void reload()
      setSyncCapability(detectSyncCapability())

      handle = registerOutboxSyncTriggers(() => {
        void syncNowRef.current()
      })
    })()

    return () => {
      unmounted = true
      handle?.dispose()
    }
  }, [reload, store])

  const recordOriginal = useCallback(
    async (fields: RecordWeatherLogPayload) => {
      setSaving(true)
      setActionError(null)
      try {
        await store.enqueue<WeatherLogOutboxPayload>({
          kind: WEATHER_LOG_OUTBOX_KIND,
          payload: { projectId, fields },
        })
        await reload()
        setSaving(false)
        // Same reasoning as `usePhotoOutbox.ts#capture`: only take a shot at syncing immediately when
        // the browser itself reports being online, so a genuinely offline save is never mistakenly
        // marked `failed` before it has any real network history.
        if (typeof navigator !== 'undefined' && navigator.onLine) {
          void syncNow()
        }
        return true
      } catch (error) {
        setSaving(false)
        setActionError(error instanceof Error ? error.message : 'บันทึกสภาพอากาศไม่สำเร็จ')
        return false
      }
    },
    [projectId, store, reload, syncNow],
  )

  /** `targetId` is whatever `WeatherCorrectionModal.onSubmit(logId, payload)` hands back — a real
   * server GUID for a row from the server-confirmed table, or a synthetic `local-pending:` id
   * (`weatherOutbox.ts#toLocalWeatherLogTargetId`) for a row from `correctableLocalOriginals` below;
   * `parseWeatherLogTargetRef` tells the two apart. */
  const recordCorrection = useCallback(
    async (targetId: string, fields: RecordWeatherLogCorrectionPayload) => {
      setSaving(true)
      setActionError(null)
      try {
        await store.enqueue<WeatherLogCorrectionOutboxPayload>({
          kind: WEATHER_LOG_CORRECTION_OUTBOX_KIND,
          payload: { projectId, target: parseWeatherLogTargetRef(targetId), fields },
        })
        await reload()
        setSaving(false)
        if (typeof navigator !== 'undefined' && navigator.onLine) {
          void syncNow()
        }
        return true
      } catch (error) {
        setSaving(false)
        setActionError(error instanceof Error ? error.message : 'บันทึกรายการแก้ไขไม่สำเร็จ')
        return false
      }
    },
    [projectId, store, reload, syncNow],
  )

  const clearActionError = useCallback(() => setActionError(null), [])

  /**
   * Local (not-yet-synced) Original items eligible for a correction — the offline analogue of
   * `weatherChain.ts#buildWeatherChainInfo`'s `isChainTail`, applied to this device's own pending
   * queue: an Original that another *local, pending* correction already targets is excluded, so the
   * UI never offers "แก้ไข" twice on the same not-yet-synced row. Deliberately does **not** attempt to
   * support correcting a pending *correction* itself (a local chain two deep) — out of scope for this
   * sprint; once the first correction syncs, the row moves into the server-confirmed table and
   * `WeatherLogTable`'s own (already-shipped) chain logic takes over correctly.
   */
  const correctableLocalOriginals = useMemo(() => {
    const targetedLocalIds = new Set(
      items
        .filter((item): item is OutboxItem<WeatherLogCorrectionOutboxPayload> => item.kind === WEATHER_LOG_CORRECTION_OUTBOX_KIND)
        .map((item) => item.payload.target)
        .filter((target): target is Extract<typeof target, { type: 'local' }> => target.type === 'local')
        .map((target) => target.outboxItemId),
    )
    return items.filter(
      (item): item is OutboxItem<WeatherLogOutboxPayload> =>
        item.kind === WEATHER_LOG_OUTBOX_KIND && item.status !== 'synced' && !targetedLocalIds.has(item.id),
    )
  }, [items])

  return {
    items,
    saving,
    actionError,
    clearActionError,
    syncCapability,
    recordOriginal,
    recordCorrection,
    syncNow,
    reload,
    correctableLocalOriginals,
  }
}

export type UseWeatherLogOutboxResult = ReturnType<typeof useWeatherLogOutbox>
