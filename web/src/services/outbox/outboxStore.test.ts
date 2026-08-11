import { describe, expect, it } from 'vitest'
import { INTERRUPTED_SYNC_ERROR, OutboxOwnerRequiredError, createOutboxStore } from './outboxStore'
import { createInMemoryOutboxStorage } from './storage.inMemory'
import type { OutboxOwner } from './types'

const OWNER_A: OutboxOwner = { userId: 'user-a', tenantId: 'tenant-1' }
const OWNER_B: OutboxOwner = { userId: 'user-b', tenantId: 'tenant-1' }
/** Cross-tenant variant of OWNER_B, used by the "different tenant" ownership cases below. */
const OWNER_B_OTHER_TENANT: OutboxOwner = { userId: 'user-b', tenantId: 'tenant-2' }

function makeStore(seed = { tick: 0, id: 0 }, getOwner: () => OutboxOwner | null = () => OWNER_A) {
  const storage = createInMemoryOutboxStorage()
  const store = createOutboxStore({
    storage,
    getOwner,
    now: () => new Date(2026, 6, 8, 9, 0, (seed.tick += 1)).toISOString(),
    generateId: () => `id-${(seed.id += 1)}`,
  })
  return { store, storage }
}

describe('outboxStore', () => {
  it('enqueue assigns a queued status, a distinct idempotency key, and zero attempts', async () => {
    const { store } = makeStore()
    const item = await store.enqueue({ kind: 'photo', payload: { caption: 'test' } })

    expect(item.status).toBe('queued')
    expect(item.attemptCount).toBe(0)
    expect(item.lastError).toBeNull()
    expect(item.serverId).toBeNull()
    expect(item.syncedAt).toBeNull()
    // The id (primary key) and the idempotency key are minted independently — never the same value
    // reused for two different purposes.
    expect(item.idempotencyKey).not.toBe(item.id)
  })

  it('mints a distinct idempotency key per enqueued item, even for identical payloads', async () => {
    const { store } = makeStore()
    const a = await store.enqueue({ kind: 'photo', payload: { caption: 'same' } })
    const b = await store.enqueue({ kind: 'photo', payload: { caption: 'same' } })

    expect(a.idempotencyKey).not.toBe(b.idempotencyKey)
  })

  it('list returns items oldest-first regardless of insertion/storage order', async () => {
    const { store } = makeStore()
    const first = await store.enqueue({ kind: 'photo', payload: {} })
    const second = await store.enqueue({ kind: 'photo', payload: {} })
    const third = await store.enqueue({ kind: 'photo', payload: {} })

    const listed = await store.list()
    expect(listed.map((i) => i.id)).toEqual([first.id, second.id, third.id])
  })

  it('list(kind) filters to only the requested kind', async () => {
    const { store } = makeStore()
    await store.enqueue({ kind: 'photo', payload: {} })
    const weatherItem = await store.enqueue({ kind: 'weather-log', payload: {} })

    const listed = await store.list('weather-log')
    expect(listed.map((i) => i.id)).toEqual([weatherItem.id])
  })

  it('pending() returns only queued and failed items, never synced or syncing', async () => {
    const { store } = makeStore()
    const queued = await store.enqueue({ kind: 'photo', payload: {} })
    const toSync = await store.enqueue({ kind: 'photo', payload: {} })
    const toFail = await store.enqueue({ kind: 'photo', payload: {} })
    const toSyncing = await store.enqueue({ kind: 'photo', payload: {} })

    await store.markSynced(toSync.id, 'server-1')
    await store.markFailed(toFail.id, 'network error')
    await store.markSyncing(toSyncing.id)

    const pending = await store.pending()
    const pendingIds = pending.map((i) => i.id).sort()
    expect(pendingIds).toEqual([queued.id, toFail.id].sort())
  })

  it('markSynced sets serverId/syncedAt, clears lastError, and drops the blob', async () => {
    const { store } = makeStore()
    const blob = new Blob(['fake-photo-bytes'], { type: 'image/jpeg' })
    const item = await store.enqueue({ kind: 'photo', payload: {}, blob, blobFileName: 'a.jpg' })
    await store.markFailed(item.id, 'first attempt failed')

    await store.markSynced(item.id, 'server-photo-id')

    const [reloaded] = await store.list()
    expect(reloaded.status).toBe('synced')
    expect(reloaded.serverId).toBe('server-photo-id')
    expect(reloaded.syncedAt).not.toBeNull()
    expect(reloaded.lastError).toBeNull()
    expect(reloaded.blob).toBeNull()
  })

  it('markFailed increments attemptCount and records the error, keeping status failed', async () => {
    const { store } = makeStore()
    const item = await store.enqueue({ kind: 'photo', payload: {} })

    await store.markFailed(item.id, 'timeout')
    await store.markFailed(item.id, 'timeout again')

    const [reloaded] = await store.list()
    expect(reloaded.status).toBe('failed')
    expect(reloaded.attemptCount).toBe(2)
    expect(reloaded.lastError).toBe('timeout again')
  })

  it('markSyncing/markSynced/markFailed on an unknown id is a safe no-op (never throws)', async () => {
    const { store } = makeStore()
    await expect(store.markSyncing('missing')).resolves.toBeUndefined()
    await expect(store.markSynced('missing', 'x')).resolves.toBeUndefined()
    await expect(store.markFailed('missing', 'x')).resolves.toBeUndefined()
  })

  it('remove deletes the item entirely', async () => {
    const { store } = makeStore()
    const item = await store.enqueue({ kind: 'photo', payload: {} })
    await store.remove(item.id)
    expect(await store.list()).toEqual([])
  })

  describe('reconcileInterruptedSyncs (S12-QA-01 defect 1 fix)', () => {
    it('resets a stale `syncing` item back to `failed` (hence pending()-eligible again)', async () => {
      const { store } = makeStore()
      const item = await store.enqueue({ kind: 'photo', payload: {} })
      await store.markSyncing(item.id)

      const reconciledCount = await store.reconcileInterruptedSyncs()

      expect(reconciledCount).toBe(1)
      const [reloaded] = await store.list()
      expect(reloaded.status).toBe('failed')
      expect(reloaded.lastError).toBe(INTERRUPTED_SYNC_ERROR)
      expect(reloaded.attemptCount).toBe(1)
      // The whole point: it is durably retryable again, not orphaned forever.
      expect((await store.pending()).map((i) => i.id)).toEqual([item.id])
    })

    it('never touches `queued`, `failed`, or `synced` items — only `syncing` ones', async () => {
      const { store } = makeStore()
      const queued = await store.enqueue({ kind: 'photo', payload: {} })
      const failed = await store.enqueue({ kind: 'photo', payload: {} })
      await store.markFailed(failed.id, 'original failure')
      const synced = await store.enqueue({ kind: 'photo', payload: {} })
      await store.markSynced(synced.id, 'server-1')

      const reconciledCount = await store.reconcileInterruptedSyncs()

      expect(reconciledCount).toBe(0)
      const byId = new Map((await store.list()).map((i) => [i.id, i]))
      expect(byId.get(queued.id)?.status).toBe('queued')
      expect(byId.get(failed.id)?.status).toBe('failed')
      expect(byId.get(failed.id)?.lastError).toBe('original failure') // untouched, not overwritten
      expect(byId.get(synced.id)?.status).toBe('synced')
    })

    it('reconciles every stale `syncing` item, not just the first', async () => {
      const { store } = makeStore()
      const a = await store.enqueue({ kind: 'photo', payload: {} })
      const b = await store.enqueue({ kind: 'photo', payload: {} })
      await store.markSyncing(a.id)
      await store.markSyncing(b.id)

      const reconciledCount = await store.reconcileInterruptedSyncs()

      expect(reconciledCount).toBe(2)
      expect((await store.pending()).map((i) => i.id).sort()).toEqual([a.id, b.id].sort())
    })

    it('scopes to the given kind, leaving another kind\'s stale syncing item alone', async () => {
      const { store } = makeStore()
      const photoItem = await store.enqueue({ kind: 'photo', payload: {} })
      const weatherItem = await store.enqueue({ kind: 'weather-log', payload: {} })
      await store.markSyncing(photoItem.id)
      await store.markSyncing(weatherItem.id)

      const reconciledCount = await store.reconcileInterruptedSyncs('photo')

      expect(reconciledCount).toBe(1)
      const byId = new Map((await store.list()).map((i) => [i.id, i]))
      expect(byId.get(photoItem.id)?.status).toBe('failed')
      expect(byId.get(weatherItem.id)?.status).toBe('syncing') // untouched — different kind
    })

    it('is a safe no-op on an empty/all-clear outbox', async () => {
      const { store } = makeStore()
      await store.enqueue({ kind: 'photo', payload: {} })

      const reconciledCount = await store.reconcileInterruptedSyncs()

      expect(reconciledCount).toBe(0)
    })
  })

  describe('ownership scoping (H-02 fix, Sprint 12 security review)', () => {
    it('enqueue stamps the current owner onto the record', async () => {
      const { store } = makeStore(undefined, () => OWNER_A)
      const item = await store.enqueue({ kind: 'photo', payload: {} })

      expect(item.ownerUserId).toBe(OWNER_A.userId)
      expect(item.ownerTenantId).toBe(OWNER_A.tenantId)
    })

    it('enqueue throws OutboxOwnerRequiredError when there is no authenticated session', async () => {
      const { store } = makeStore(undefined, () => null)
      await expect(store.enqueue({ kind: 'photo', payload: {} })).rejects.toBeInstanceOf(OutboxOwnerRequiredError)
    })

    it("list() never returns another owner's items — the two-owner case", async () => {
      const storage = createInMemoryOutboxStorage()
      const seed = { tick: 0, id: 0 }
      const now = () => new Date(2026, 6, 8, 9, 0, (seed.tick += 1)).toISOString()
      const generateId = () => `id-${(seed.id += 1)}`

      const storeA = createOutboxStore({ storage, getOwner: () => OWNER_A, now, generateId })
      const storeB = createOutboxStore({ storage, getOwner: () => OWNER_B, now, generateId })

      const itemA = await storeA.enqueue({ kind: 'photo', payload: { caption: 'A private' } })
      const itemB = await storeB.enqueue({ kind: 'photo', payload: { caption: 'B private' } })

      expect((await storeA.list()).map((i) => i.id)).toEqual([itemA.id])
      expect((await storeB.list()).map((i) => i.id)).toEqual([itemB.id])
    })

    it("pending() never drains another owner's queued items — the exact H-02 attack scenario", async () => {
      const storage = createInMemoryOutboxStorage()
      const seed = { tick: 0, id: 0 }
      const now = () => new Date(2026, 6, 8, 9, 0, (seed.tick += 1)).toISOString()
      const generateId = () => `id-${(seed.id += 1)}`

      // A captures a photo offline, then hands the (shared) tablet to B without syncing.
      const storeA = createOutboxStore({ storage, getOwner: () => OWNER_A, now, generateId })
      await storeA.enqueue({ kind: 'photo', payload: { caption: "A's unsynced photo" } })

      // B signs in on the same device/database — B's own session must never see, let alone flush,
      // A's still-queued item.
      const storeB = createOutboxStore({ storage, getOwner: () => OWNER_B, now, generateId })
      expect(await storeB.pending()).toEqual([])
      expect(await storeB.list()).toEqual([])
    })

    it('cross-tenant owners (same userId string reused, different tenantId) are also isolated', async () => {
      const storage = createInMemoryOutboxStorage()
      const seed = { tick: 0, id: 0 }
      const now = () => new Date(2026, 6, 8, 9, 0, (seed.tick += 1)).toISOString()
      const generateId = () => `id-${(seed.id += 1)}`

      const storeB1 = createOutboxStore({ storage, getOwner: () => OWNER_B, now, generateId })
      await storeB1.enqueue({ kind: 'photo', payload: {} })

      const storeB2 = createOutboxStore({ storage, getOwner: () => OWNER_B_OTHER_TENANT, now, generateId })
      expect(await storeB2.list()).toEqual([])
      expect(await storeB2.pending()).toEqual([])
    })

    it('reconcileInterruptedSyncs never revives a stale `syncing` item left by a different owner', async () => {
      const storage = createInMemoryOutboxStorage()
      const seed = { tick: 0, id: 0 }
      const now = () => new Date(2026, 6, 8, 9, 0, (seed.tick += 1)).toISOString()
      const generateId = () => `id-${(seed.id += 1)}`

      const storeA = createOutboxStore({ storage, getOwner: () => OWNER_A, now, generateId })
      const stranded = await storeA.enqueue({ kind: 'photo', payload: {} })
      await storeA.markSyncing(stranded.id)

      const storeB = createOutboxStore({ storage, getOwner: () => OWNER_B, now, generateId })
      const reconciledByB = await storeB.reconcileInterruptedSyncs()
      expect(reconciledByB).toBe(0)

      // A's item is untouched — still genuinely `syncing`, not silently revived on B's behalf.
      const [reloaded] = await storeA.list()
      expect(reloaded.status).toBe('syncing')

      // A's own session still reconciles it normally.
      const reconciledByA = await storeA.reconcileInterruptedSyncs()
      expect(reconciledByA).toBe(1)
    })

    it('list()/pending() return nothing when nobody is authenticated (fail closed, not open)', async () => {
      // Two stores share the same underlying storage so this proves a read-time filter, not merely
      // "a fresh empty store" answering the null-owner reads.
      const sharedStorage = createInMemoryOutboxStorage()
      const authedStore = createOutboxStore({ storage: sharedStorage, getOwner: () => OWNER_A })
      await authedStore.enqueue({ kind: 'photo', payload: {} })
      const loggedOutStore = createOutboxStore({ storage: sharedStorage, getOwner: () => null })

      expect(await loggedOutStore.list()).toEqual([])
      expect(await loggedOutStore.pending()).toEqual([])
      // Sanity: the item genuinely exists in storage, just correctly hidden from a null-owner read.
      expect(await authedStore.list()).toHaveLength(1)
    })

    it('a record with a missing/malformed owner (pre-H-02 legacy data) is never shown to anyone', async () => {
      const storage = createInMemoryOutboxStorage()
      // Simulate a record written before this migration by writing directly to storage, bypassing
      // `enqueue` (which now always stamps an owner).
      await storage.put({
        id: 'legacy-item',
        kind: 'photo',
        idempotencyKey: 'idem-legacy',
        // Intentionally cast through `unknown` — a real pre-migration IndexedDB record simply lacks
        // these fields at runtime despite what the (post-migration) static type now requires.
        ...({} as { ownerUserId: string; ownerTenantId: string }),
        payload: {},
        blob: null,
        blobFileName: null,
        blobContentType: null,
        status: 'queued',
        attemptCount: 0,
        lastError: null,
        createdAt: '2026-07-01T00:00:00.000Z',
        updatedAt: '2026-07-01T00:00:00.000Z',
        syncedAt: null,
        serverId: null,
      })

      const storeA = createOutboxStore({ storage, getOwner: () => OWNER_A })
      const storeB = createOutboxStore({ storage, getOwner: () => OWNER_B })

      expect(await storeA.list()).toEqual([])
      expect(await storeB.list()).toEqual([])
    })
  })
})
