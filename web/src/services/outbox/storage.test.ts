// jsdom (this project's vitest `environment`) has no native IndexedDB implementation — confirmed
// directly against the installed jsdom package (`node_modules/jsdom` ships no IDBFactory/IndexedDB
// files at all). `fake-indexeddb/auto` installs a real, spec-conformant IndexedDB implementation
// (transactions, indexes, structured-clone `Blob` support) onto the global scope for the duration of
// this file only, so `createIndexedDbOutboxStorage`'s actual IndexedDB calls run against real
// IndexedDB semantics rather than a hand-rolled mock that could diverge from browser behaviour.
import 'fake-indexeddb/auto'
// jsdom's own `Blob` global is not the same class Node's built-in `structuredClone` (which
// `fake-indexeddb` clones stored values with) knows how to clone — cloning a jsdom `Blob` silently
// produces `{}` rather than throwing, confirmed directly by an isolated repro before writing this
// workaround. Node's native `Blob` (identical Blob interface, just registered with V8's structured-
// clone serializer) round-trips correctly and is what a real browser's own engine-native `Blob`
// would do anyway — this is a test-environment gap between two different JS realms, not something
// `createIndexedDbOutboxStorage`'s own code needs to work around, so only the test's Blob
// construction changes here.
import { Blob as NodeBlob } from 'node:buffer'
import { beforeEach, describe, expect, it } from 'vitest'
import { createIndexedDbOutboxStorage } from './storage'
import type { OutboxItem } from './types'

function makeItem(overrides: Partial<OutboxItem> = {}): OutboxItem {
  return {
    id: 'item-1',
    kind: 'photo',
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: { caption: 'test' },
    blob: null,
    blobFileName: null,
    blobContentType: null,
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

describe('createIndexedDbOutboxStorage (real IndexedDB via fake-indexeddb)', () => {
  // A fresh database name per test avoids cross-test bleed-through on the shared fake-indexeddb
  // global registry (`indexedDB.databases()` persists for the process, unlike a real browser tab
  // reload) without needing a global teardown hook.
  let dbName: string
  beforeEach(() => {
    dbName = `cmplus-outbox-test-${Math.random().toString(36).slice(2)}`
  })

  it('put then get round-trips the item exactly', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    const item = makeItem()

    await storage.put(item)
    const loaded = await storage.get(item.id)

    expect(loaded).toEqual(item)
  })

  it('get on a missing id resolves to undefined', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    await expect(storage.get('missing')).resolves.toBeUndefined()
  })

  it('put with the same id overwrites the previous value', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    await storage.put(makeItem({ status: 'queued' }))
    await storage.put(makeItem({ status: 'synced', serverId: 'server-1' }))

    const loaded = await storage.get('item-1')
    expect(loaded?.status).toBe('synced')
    expect(loaded?.serverId).toBe('server-1')
  })

  it('list() returns every stored item; list(kind) filters', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    await storage.put(makeItem({ id: 'p-1', kind: 'photo' }))
    await storage.put(makeItem({ id: 'w-1', kind: 'weather-log' }))

    const all = await storage.list()
    expect(all.map((i) => i.id).sort()).toEqual(['p-1', 'w-1'])

    const onlyPhotos = await storage.list('photo')
    expect(onlyPhotos.map((i) => i.id)).toEqual(['p-1'])
  })

  it('delete removes the item', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    await storage.put(makeItem())
    await storage.delete('item-1')

    expect(await storage.get('item-1')).toBeUndefined()
    expect(await storage.list()).toEqual([])
  })

  it('stores and retrieves a real Blob attachment intact', async () => {
    const storage = createIndexedDbOutboxStorage(dbName)
    const blob = new NodeBlob(['fake-compressed-jpeg-bytes'], { type: 'image/jpeg' })
    await storage.put(makeItem({ blob, blobFileName: 'site.jpg', blobContentType: 'image/jpeg' }))

    const loaded = await storage.get('item-1')
    expect(loaded?.blob).toBeInstanceOf(NodeBlob)
    expect(loaded?.blob?.type).toBe('image/jpeg')
    expect(await loaded?.blob?.text()).toBe('fake-compressed-jpeg-bytes')
  })
})
