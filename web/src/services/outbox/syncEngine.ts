import { OutboxConflictError } from './errors'
import type { OutboxStore } from './outboxStore'
import type { OutboxItem } from './types'

/** One `kind`'s upload function: turns a queued item into a server id, or throws. Registered per
 * `kind` in `SyncEngineOptions.uploaders` — `features/photo/photoOutbox.ts` registers `'photo'`;
 * S13-FE-01 registers `'weather-log'`/`'progress-batch'` alongside it, in `web/src/services/outbox/`
 * per `docs/10.`'s own artifact path for that task — this file does not change to add them. */
export type OutboxUploader<TPayload = unknown> = (item: OutboxItem<TPayload>) => Promise<{ serverId: string }>

export interface SyncEngineOptions {
  store: OutboxStore
  uploaders: Record<string, OutboxUploader>
  onItemSynced?: (item: OutboxItem) => void
  onItemFailed?: (item: OutboxItem, error: unknown) => void
  /** S13-FE-01: called when an uploader throws `OutboxConflictError` — see that type's doc comment. */
  onItemConflicted?: (item: OutboxItem, error: unknown) => void
}

export interface FlushResult {
  synced: number
  failed: number
  /** S13-FE-01: items moved to the terminal `'conflict'` status this pass — see
   * `errors.ts#OutboxConflictError`. Not a subset of `failed`. */
  conflicted: number
  /** Items whose `kind` has no registered uploader — left `queued`, not counted as `failed` (a
   * missing uploader is a programming error to fix, not a transient network failure to retry into
   * the same failed-with-backoff bucket). */
  skippedUnknownKind: number
}

function toErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  return typeof error === 'string' ? error : 'อัปโหลดไม่สำเร็จ'
}

/**
 * Drives one queue-drain pass (S12-FE-01's "เน็ตกลับมา → Background Sync อัปโหลดเองและขึ้น 'ซิงค์แล้ว'"
 * DoD, and its documented fallback — see `syncTriggers.ts` for what actually calls `flush()` given
 * Background Sync's own unreliability). Kind-agnostic: it only knows how to walk the pending set and
 * hand each item to whichever uploader is registered for its `kind`, so extending the outbox to a new
 * kind (S13-FE-01) never touches this file.
 *
 * Processes items **sequentially, not in parallel** — deliberately: a site upload is already
 * bandwidth-constrained (the whole reason the item is queued), and a burst of parallel multipart
 * uploads competing for the same thin/flaky connection is more likely to make all of them time out
 * than to finish sooner. A single in-flight `flush()` guard (`syncing` below) additionally prevents
 * two overlapping triggers (e.g. an `online` event firing while a manual "ซิงค์เดี๋ยวนี้" tap is still
 * running) from double-uploading the same item.
 */
export function createSyncEngine(options: SyncEngineOptions) {
  const { store, uploaders, onItemSynced, onItemFailed, onItemConflicted } = options
  let syncing = false

  async function flush(): Promise<FlushResult> {
    if (syncing) {
      return { synced: 0, failed: 0, conflicted: 0, skippedUnknownKind: 0 }
    }

    syncing = true
    let synced = 0
    let failed = 0
    let conflicted = 0
    let skippedUnknownKind = 0

    try {
      const pendingItems = await store.pending()

      for (const item of pendingItems) {
        const uploader = uploaders[item.kind]
        if (!uploader) {
          skippedUnknownKind += 1
          continue
        }

        await store.markSyncing(item.id)
        try {
          const result = await uploader(item)
          await store.markSynced(item.id, result.serverId)
          synced += 1
          onItemSynced?.({ ...item, status: 'synced', serverId: result.serverId })
        } catch (error) {
          const message = toErrorMessage(error)
          if (error instanceof OutboxConflictError) {
            // S13-FE-01: a same-key-different-payload (or non-replayable) 409 — the queued item
            // disagrees with what the server already accepted, so blind retry is never scheduled for
            // it again (see `errors.ts#OutboxConflictError`, `outboxStore.ts#markConflict`).
            await store.markConflict(item.id, message)
            conflicted += 1
            onItemConflicted?.({ ...item, status: 'conflict', lastError: message }, error)
          } else {
            await store.markFailed(item.id, message)
            failed += 1
            onItemFailed?.({ ...item, status: 'failed', lastError: message }, error)
          }
        }
      }

      return { synced, failed, conflicted, skippedUnknownKind }
    } finally {
      syncing = false
    }
  }

  return { flush, get isSyncing() { return syncing } }
}

export type SyncEngine = ReturnType<typeof createSyncEngine>
