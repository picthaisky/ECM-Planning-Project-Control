import type { OutboxStorageAdapter } from './storage'
import type { OutboxItem } from './types'

/**
 * In-process `OutboxStorageAdapter` test double — used by `outboxStore.test.ts`/`syncEngine.test.ts`
 * so the business-logic tests do not depend on IndexedDB semantics at all (the real adapter's own
 * transaction/index behaviour is exercised separately in `storage.test.ts` against a real IndexedDB
 * polyfilled by `fake-indexeddb`). Shallow-copies each item on write/read so a caller mutating the
 * object it got back cannot corrupt this store's internal state out from under it — the same
 * write-then-read isolation a real IndexedDB structured-clone boundary gives for free, without
 * depending on `structuredClone`'s own `Blob` support (inconsistent across Node/jsdom versions).
 */
export function createInMemoryOutboxStorage(): OutboxStorageAdapter {
  const items = new Map<string, OutboxItem>()

  return {
    async put(item) {
      items.set(item.id, { ...item })
    },
    async get(id) {
      const found = items.get(id)
      return found ? { ...found } : undefined
    },
    async list(kind) {
      const all = Array.from(items.values()).map((item) => ({ ...item }))
      return kind ? all.filter((item) => item.kind === kind) : all
    },
    async delete(id) {
      items.delete(id)
    },
    // Atomic in this single-threaded double by construction: the read and the conditional write both
    // run to completion synchronously within this one async tick, so nothing can interleave — the
    // same lost-update-proof property the real adapter gets from a readwrite transaction (N-02).
    async mutate(id, apply) {
      const current = items.get(id)
      if (current === undefined) {
        return false
      }
      const next = apply({ ...current })
      if (next === undefined) {
        return false
      }
      items.set(id, { ...next })
      return true
    },
  }
}
