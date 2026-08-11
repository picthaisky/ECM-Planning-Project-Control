import { describe, expect, it, vi } from 'vitest'
import { createOutboxStore } from './outboxStore'
import { createInMemoryOutboxStorage } from './storage.inMemory'
import { createSyncEngine } from './syncEngine'
import type { OutboxUploader } from './syncEngine'

const TEST_OWNER = { userId: 'user-a', tenantId: 'tenant-1' }

function makeStore() {
  let tick = 0
  let id = 0
  const storage = createInMemoryOutboxStorage()
  return createOutboxStore({
    storage,
    getOwner: () => TEST_OWNER,
    now: () => new Date(2026, 6, 8, 9, 0, (tick += 1)).toISOString(),
    generateId: () => `id-${(id += 1)}`,
  })
}

describe('syncEngine.flush', () => {
  it('uploads every pending item and marks each synced with the returned serverId', async () => {
    const store = makeStore()
    await store.enqueue({ kind: 'photo', payload: { caption: 'a' } })
    await store.enqueue({ kind: 'photo', payload: { caption: 'b' } })

    const uploader: OutboxUploader = vi.fn(async (item) => ({ serverId: `server-${String(item.payload)}` }))
    const engine = createSyncEngine({ store, uploaders: { photo: uploader } })

    const result = await engine.flush()

    expect(result).toEqual({ synced: 2, failed: 0, skippedUnknownKind: 0 })
    expect(uploader).toHaveBeenCalledTimes(2)
    const remaining = await store.pending()
    expect(remaining).toEqual([])
    const all = await store.list()
    expect(all.every((item) => item.status === 'synced')).toBe(true)
  })

  it('marks an item failed (not lost) when its uploader throws, and leaves it pending for retry', async () => {
    const store = makeStore()
    const item = await store.enqueue({ kind: 'photo', payload: {} })

    const uploader: OutboxUploader = vi.fn(async () => {
      throw new Error('เครือข่ายขัดข้อง')
    })
    const engine = createSyncEngine({ store, uploaders: { photo: uploader } })

    const result = await engine.flush()

    expect(result).toEqual({ synced: 0, failed: 1, skippedUnknownKind: 0 })
    const [reloaded] = await store.list()
    expect(reloaded.id).toBe(item.id)
    expect(reloaded.status).toBe('failed')
    expect(reloaded.lastError).toBe('เครือข่ายขัดข้อง')
    expect(reloaded.attemptCount).toBe(1)

    // A previously-failed item is still picked up by the next flush (queued/failed both "pending").
    const pendingBeforeRetry = await store.pending()
    expect(pendingBeforeRetry.map((i) => i.id)).toEqual([item.id])
  })

  it('a later successful retry recovers a previously-failed item', async () => {
    const store = makeStore()
    await store.enqueue({ kind: 'photo', payload: {} })

    let attempt = 0
    const uploader: OutboxUploader = vi.fn(async () => {
      attempt += 1
      if (attempt === 1) throw new Error('first attempt fails')
      return { serverId: 'server-ok' }
    })
    const engine = createSyncEngine({ store, uploaders: { photo: uploader } })

    const first = await engine.flush()
    expect(first).toEqual({ synced: 0, failed: 1, skippedUnknownKind: 0 })

    const second = await engine.flush()
    expect(second).toEqual({ synced: 1, failed: 0, skippedUnknownKind: 0 })
    expect(await store.pending()).toEqual([])
  })

  it('leaves an item queued (not failed) when no uploader is registered for its kind', async () => {
    const store = makeStore()
    await store.enqueue({ kind: 'weather-log', payload: {} })

    const engine = createSyncEngine({ store, uploaders: { photo: vi.fn() } })
    const result = await engine.flush()

    expect(result).toEqual({ synced: 0, failed: 0, skippedUnknownKind: 1 })
    const [reloaded] = await store.list()
    expect(reloaded.status).toBe('queued')
  })

  it('a second concurrent flush() call is a safe no-op while one is already in flight', async () => {
    const store = makeStore()
    await store.enqueue({ kind: 'photo', payload: {} })

    let resolveUpload: (value: { serverId: string }) => void = () => {}
    const uploader: OutboxUploader = vi.fn(
      () =>
        new Promise<{ serverId: string }>((resolve) => {
          resolveUpload = resolve
        }),
    )
    const engine = createSyncEngine({ store, uploaders: { photo: uploader } })

    const firstFlush = engine.flush()
    // The `syncing` guard is set synchronously the instant `flush()` is called — before its own
    // first `await` — so a second call made here (still before `firstFlush`'s internal chain has had
    // any microtask tick to run) deterministically observes it and returns immediately. This does
    // NOT mean `firstFlush` has progressed as far as calling `uploader` yet (its own chain has
    // several `await`s of its own storage calls ahead of that) — that is asserted separately below,
    // after `firstFlush` is given the chance to run to completion.
    const secondFlush = await engine.flush()

    expect(secondFlush).toEqual({ synced: 0, failed: 0, skippedUnknownKind: 0 })

    // `firstFlush`'s own chain (its `store.pending()`/`store.markSyncing()` calls) needs a few more
    // microtask ticks than `secondFlush`'s immediate early-return before it actually reaches the
    // `uploader(item)` call (which is what sets the real `resolveUpload` via closure, replacing the
    // no-op default) — `vi.waitFor` here, not a fixed number of `await Promise.resolve()` hops, so
    // this does not silently start relying on the in-memory store's exact call depth.
    await vi.waitFor(() => expect(uploader).toHaveBeenCalledTimes(1))
    resolveUpload({ serverId: 'server-1' })
    await firstFlush

    // Exactly one upload attempt total — the concurrent second call never re-processed the item.
    expect(uploader).toHaveBeenCalledTimes(1)
  })

  it('calls onItemSynced/onItemFailed callbacks with the outcome', async () => {
    const store = makeStore()
    await store.enqueue({ kind: 'photo', payload: {} })
    await store.enqueue({ kind: 'photo', payload: {} })

    let call = 0
    const uploader: OutboxUploader = vi.fn(async () => {
      call += 1
      if (call === 1) return { serverId: 'ok' }
      throw new Error('boom')
    })

    const onItemSynced = vi.fn()
    const onItemFailed = vi.fn()
    const engine = createSyncEngine({ store, uploaders: { photo: uploader }, onItemSynced, onItemFailed })

    await engine.flush()

    expect(onItemSynced).toHaveBeenCalledTimes(1)
    expect(onItemFailed).toHaveBeenCalledTimes(1)
  })
})
