import { describe, expect, it } from 'vitest'
import { DEFAULT_SYNCED_RETENTION_MS, purgeExpiredSyncedItems, quarantineOwnerBlobs } from './outboxMaintenance'
import { createInMemoryOutboxStorage } from './storage.inMemory'
import type { OutboxStorageAdapter } from './storage'
import type { OutboxItem } from './types'

const OWNER_A = { userId: 'user-a', tenantId: 'tenant-1' }
const OWNER_B = { userId: 'user-b', tenantId: 'tenant-1' }

function makeItem(overrides: Partial<OutboxItem> = {}): OutboxItem {
  return {
    id: 'item-1',
    kind: 'photo',
    idempotencyKey: 'idem-1',
    ownerUserId: OWNER_A.userId,
    ownerTenantId: OWNER_A.tenantId,
    payload: { caption: 'test' },
    blob: new Blob(['bytes'], { type: 'image/jpeg' }),
    blobFileName: 'a.jpg',
    blobContentType: 'image/jpeg',
    status: 'queued',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-07-08T09:00:00.000Z',
    updatedAt: '2026-07-08T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

describe('quarantineOwnerBlobs (H-02 fix bullet 2, Sprint 12 security review)', () => {
  it("drops the blob from the owner's queued/failed/syncing items, leaving the rest of the record intact", async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(makeItem({ id: 'queued-1', status: 'queued' }))
    await storage.put(makeItem({ id: 'failed-1', status: 'failed', lastError: 'boom' }))
    await storage.put(makeItem({ id: 'syncing-1', status: 'syncing' }))

    const cleared = await quarantineOwnerBlobs(storage, OWNER_A)

    expect(cleared).toBe(3)
    const all = await storage.list()
    for (const item of all) {
      expect(item.blob).toBeNull()
    }
    // Metadata survives — this is a quarantine, not a delete.
    const failed = all.find((item) => item.id === 'failed-1')
    expect(failed?.lastError).toBe('boom')
    expect(failed?.payload).toEqual({ caption: 'test' })
  })

  it("never touches another owner's items", async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(makeItem({ id: 'a-1', ownerUserId: OWNER_A.userId, ownerTenantId: OWNER_A.tenantId }))
    await storage.put(makeItem({ id: 'b-1', ownerUserId: OWNER_B.userId, ownerTenantId: OWNER_B.tenantId }))

    const cleared = await quarantineOwnerBlobs(storage, OWNER_A)

    expect(cleared).toBe(1)
    const all = await storage.list()
    expect(all.find((item) => item.id === 'a-1')?.blob).toBeNull()
    expect(all.find((item) => item.id === 'b-1')?.blob).not.toBeNull()
  })

  it('is a no-op (returns 0) when the owner has no items with a blob (e.g. already synced)', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(makeItem({ id: 'synced-1', status: 'synced', blob: null, serverId: 'server-1' }))

    const cleared = await quarantineOwnerBlobs(storage, OWNER_A)

    expect(cleared).toBe(0)
  })

  it('is a safe no-op on an empty outbox', async () => {
    const storage = createInMemoryOutboxStorage()
    await expect(quarantineOwnerBlobs(storage, OWNER_A)).resolves.toBe(0)
  })

  it('does not revert a concurrent markSynced that lands between its list snapshot and its write (N-02)', async () => {
    // Reproduces the exact lost-update window the review describes: logout quarantine runs while an
    // in-flight upload completes. The old list-snapshot-then-put shape wrote the whole stale item
    // back, reverting a just-synced record to status=syncing/serverId=null and telling the user to
    // re-shoot a photo the server already held. This test forces the interleave via a storage wrapper
    // whose list() returns the pre-sync snapshot but then immediately commits the concurrent
    // markSynced — so it fails against the old code and passes only with the mutate()-based fix.
    const inner = createInMemoryOutboxStorage()
    await inner.put(makeItem({ id: 'racing', status: 'syncing' }))

    let raced = false
    const storage: OutboxStorageAdapter = {
      ...inner,
      list: async (kind) => {
        const snapshot = await inner.list(kind) // stale: still shows blob present, status=syncing
        if (!raced) {
          raced = true
          await inner.mutate('racing', (current) => ({
            ...current,
            status: 'synced',
            serverId: 'srv-1',
            syncedAt: '2026-07-08T10:00:00.000Z',
            blob: null,
          }))
        }
        return snapshot
      },
    }

    const cleared = await quarantineOwnerBlobs(storage, OWNER_A)

    const after = await inner.get('racing')
    expect(after?.status).toBe('synced') // NOT reverted to 'syncing'
    expect(after?.serverId).toBe('srv-1') // NOT reverted to null
    expect(after?.blob).toBeNull() // still quarantined, exactly once
    expect(cleared).toBe(0) // mutate re-read blob===null → genuine no-op, not a stale overwrite
  })
})

describe('purgeExpiredSyncedItems (H-02 fix bullet 3, Sprint 12 security review)', () => {
  it('deletes a synced record older than the retention window', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(
      makeItem({
        id: 'old-synced',
        status: 'synced',
        blob: null,
        serverId: 'server-1',
        syncedAt: '2026-01-01T00:00:00.000Z',
      }),
    )

    const deleted = await purgeExpiredSyncedItems(storage, {
      now: () => '2026-01-10T00:00:00.000Z', // 9 days later, past the 7-day default
    })

    expect(deleted).toBe(1)
    expect(await storage.list()).toEqual([])
  })

  it('leaves a synced record inside the retention window untouched', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(
      makeItem({
        id: 'recent-synced',
        status: 'synced',
        blob: null,
        serverId: 'server-1',
        syncedAt: '2026-01-08T00:00:00.000Z',
      }),
    )

    const deleted = await purgeExpiredSyncedItems(storage, { now: () => '2026-01-09T00:00:00.000Z' })

    expect(deleted).toBe(0)
    expect(await storage.list()).toHaveLength(1)
  })

  it('never deletes queued/failed/syncing items, no matter how old', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(makeItem({ id: 'old-queued', status: 'queued', createdAt: '2020-01-01T00:00:00.000Z' }))
    await storage.put(makeItem({ id: 'old-failed', status: 'failed', createdAt: '2020-01-01T00:00:00.000Z' }))

    const deleted = await purgeExpiredSyncedItems(storage, { now: () => '2026-01-01T00:00:00.000Z' })

    expect(deleted).toBe(0)
    expect(await storage.list()).toHaveLength(2)
  })

  it('sweeps every owner, not only one — device-wide housekeeping, not owner-scoped', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(
      makeItem({
        id: 'a-old',
        ownerUserId: OWNER_A.userId,
        ownerTenantId: OWNER_A.tenantId,
        status: 'synced',
        blob: null,
        syncedAt: '2020-01-01T00:00:00.000Z',
      }),
    )
    await storage.put(
      makeItem({
        id: 'b-old',
        ownerUserId: OWNER_B.userId,
        ownerTenantId: OWNER_B.tenantId,
        status: 'synced',
        blob: null,
        syncedAt: '2020-01-01T00:00:00.000Z',
      }),
    )

    const deleted = await purgeExpiredSyncedItems(storage, { now: () => '2026-01-01T00:00:00.000Z' })

    expect(deleted).toBe(2)
    expect(await storage.list()).toEqual([])
  })

  it('respects an explicit kind filter', async () => {
    const storage = createInMemoryOutboxStorage()
    await storage.put(
      makeItem({ id: 'photo-old', kind: 'photo', status: 'synced', blob: null, syncedAt: '2020-01-01T00:00:00.000Z' }),
    )
    await storage.put(
      makeItem({
        id: 'weather-old',
        kind: 'weather-log',
        status: 'synced',
        blob: null,
        syncedAt: '2020-01-01T00:00:00.000Z',
      }),
    )

    const deleted = await purgeExpiredSyncedItems(storage, { now: () => '2026-01-01T00:00:00.000Z', kind: 'photo' })

    expect(deleted).toBe(1)
    const remaining = await storage.list()
    expect(remaining.map((i) => i.id)).toEqual(['weather-old'])
  })

  it('uses a 7-day default retention window', () => {
    expect(DEFAULT_SYNCED_RETENTION_MS).toBe(7 * 24 * 60 * 60 * 1000)
  })
})
