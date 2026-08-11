import type { OutboxItem } from './types'

/**
 * Storage seam for the outbox (S12-FE-01). `outboxStore.ts`'s business logic (enqueue/status
 * transitions/idempotency) is written against this interface, never against `indexedDB` directly —
 * so it is unit-testable with any conforming implementation. `createIndexedDbOutboxStorage` below is
 * the real, production implementation; tests may substitute `createInMemoryOutboxStorage`
 * (`storage.inMemory.ts`) or run against a real IndexedDB polyfilled by `fake-indexeddb` in the test
 * environment (jsdom has no native IndexedDB — see that test file's own remarks).
 */
export interface OutboxStorageAdapter {
  put(item: OutboxItem): Promise<void>
  get(id: string): Promise<OutboxItem | undefined>
  /** All items, optionally filtered to one `kind`. No ordering guarantee beyond "stable enough for a
   * UI list" — callers that need a specific order (e.g. oldest-first) sort client-side. */
  list(kind?: string): Promise<OutboxItem[]>
  delete(id: string): Promise<void>
}

const DB_NAME = 'cmplus-outbox'
const DB_VERSION = 1
const STORE_NAME = 'items'
const KIND_INDEX = 'by-kind'

function openDatabase(dbName: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(dbName, DB_VERSION)

    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        const store = db.createObjectStore(STORE_NAME, { keyPath: 'id' })
        store.createIndex(KIND_INDEX, 'kind', { unique: false })
      }
    }

    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('เปิดฐานข้อมูล IndexedDB ไม่สำเร็จ'))
  })
}

function promisifyRequest<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('ดำเนินการกับ IndexedDB ไม่สำเร็จ'))
  })
}

/**
 * Real, production `OutboxStorageAdapter` backed by the browser's IndexedDB — one database
 * (`cmplus-outbox`), one object store (`items`, keyed by the client-generated `id`), one index on
 * `kind` so a future multi-kind outbox (S13-FE-01) can list by kind without a full table scan.
 *
 * A fresh connection is opened per call rather than held open for the module's lifetime: this
 * module has no teardown hook to close a long-lived connection on, and IndexedDB connections are
 * cheap enough (no observed cost in this codebase's other IndexedDB-adjacent work) that reopening
 * per call is the simpler, leak-free default. Blobs are stored directly as IndexedDB values (the
 * spec supports structured-clonable `Blob`s natively — no base64 round-trip, which would both bloat
 * storage ~33% and burn CPU on a site phone).
 */
export function createIndexedDbOutboxStorage(dbName: string = DB_NAME): OutboxStorageAdapter {
  async function withStore<T>(mode: IDBTransactionMode, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
    const db = await openDatabase(dbName)
    try {
      const tx = db.transaction(STORE_NAME, mode)
      const store = tx.objectStore(STORE_NAME)
      const result = await promisifyRequest(run(store))
      await new Promise<void>((resolve, reject) => {
        tx.oncomplete = () => resolve()
        tx.onerror = () => reject(tx.error ?? new Error('ธุรกรรม IndexedDB ล้มเหลว'))
        tx.onabort = () => reject(tx.error ?? new Error('ธุรกรรม IndexedDB ถูกยกเลิก'))
      })
      return result
    } finally {
      db.close()
    }
  }

  return {
    put: async (item) => {
      await withStore<IDBValidKey>('readwrite', (store) => store.put(item))
    },
    get: (id) => withStore<OutboxItem | undefined>('readonly', (store) => store.get(id)),
    list: async (kind) => {
      const all = await withStore<OutboxItem[]>('readonly', (store) => store.getAll())
      return kind ? all.filter((item) => item.kind === kind) : all
    },
    delete: async (id) => {
      await withStore<undefined>('readwrite', (store) => store.delete(id))
    },
  }
}
