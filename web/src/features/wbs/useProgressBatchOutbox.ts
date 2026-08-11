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
import { PROGRESS_BATCH_OUTBOX_KIND, uploadProgressBatchOutboxItem } from './progressOutbox'
import type { ProgressBatchOutboxPayload } from './progressOutbox'
import type { BatchProgressRequest } from './types'

/**
 * S13-FE-01: the WBS batch "โหมดอัปเดตความคืบหน้า" grid's offline capture -> outbox -> sync flow,
 * mirroring `features/photo/usePhotoOutbox.ts`'s established shape exactly (one kind, one enqueued
 * item per submit, enqueue-then-best-effort-immediate-sync). `useBatchProgressForm.ts` composes this
 * hook rather than calling `batchRecordProgress` directly, so the grid's own row/validation/decrease-
 * confirmation logic is unchanged and only the final "how is this actually sent" step moves onto the
 * outbox.
 */
export function useProgressBatchOutbox(projectId: string, onSynced?: () => void) {
  const [items, setItems] = useState<OutboxItem<ProgressBatchOutboxPayload>[]>([])
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
        uploaders: { [PROGRESS_BATCH_OUTBOX_KIND]: uploadProgressBatchOutboxItem as OutboxUploader },
      }),
    [store],
  )

  const reload = useCallback(async () => {
    const loaded = (await store.list(PROGRESS_BATCH_OUTBOX_KIND)) as OutboxItem<ProgressBatchOutboxPayload>[]
    setItems(loaded.filter((item) => item.payload.projectId === projectId))
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
      await store.reconcileInterruptedSyncs(PROGRESS_BATCH_OUTBOX_KIND)
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

  const enqueueBatch = useCallback(
    async (request: BatchProgressRequest) => {
      await store.enqueue<ProgressBatchOutboxPayload>({
        kind: PROGRESS_BATCH_OUTBOX_KIND,
        payload: { projectId, request },
      })
      await reload()
      // Same reasoning as `usePhotoOutbox.ts#capture`: only take a shot at syncing immediately when
      // the browser itself reports being online.
      if (typeof navigator !== 'undefined' && navigator.onLine) {
        void syncNow()
      }
    },
    [projectId, store, reload, syncNow],
  )

  return { items, syncCapability, enqueueBatch, syncNow, reload }
}

export type UseProgressBatchOutboxResult = ReturnType<typeof useProgressBatchOutbox>
