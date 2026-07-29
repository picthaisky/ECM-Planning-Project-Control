import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useProjectStore } from './projectStore'

/**
 * This Vitest/jsdom combination does not reliably expose a working `window.localStorage`
 * (`window === globalThis` here, and jsdom's `localStorage` accessor does not survive being
 * copied onto it — confirmed empirically, not a guess) — Node's own experimental global
 * `localStorage` warning is a symptom of the same gap, not the cause. Stubbing a minimal in-memory
 * `Storage` directly is the robust fix, independent of that environment quirk.
 */
function createMemoryStorage(): Storage {
  const data = new Map<string, string>()
  return {
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => {
      data.set(key, value)
    },
    removeItem: (key: string) => {
      data.delete(key)
    },
    clear: () => data.clear(),
    key: (index: number) => Array.from(data.keys())[index] ?? null,
    get length() {
      return data.size
    },
  } as Storage
}

describe('projectStore', () => {
  let storage: Storage

  beforeEach(() => {
    storage = createMemoryStorage()
    vi.stubGlobal('localStorage', storage)
    useProjectStore.getState().clearCurrentProjectId()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('starts with no current project', () => {
    expect(useProjectStore.getState().currentProjectId).toBeNull()
  })

  it('setCurrentProjectId stores and persists the id', () => {
    useProjectStore.getState().setCurrentProjectId('11111111-1111-1111-1111-111111111111')

    expect(useProjectStore.getState().currentProjectId).toBe(
      '11111111-1111-1111-1111-111111111111',
    )
    expect(storage.getItem('cmplus.currentProjectId')).toBe('11111111-1111-1111-1111-111111111111')
  })

  it('clearCurrentProjectId removes it from the store and storage', () => {
    useProjectStore.getState().setCurrentProjectId('11111111-1111-1111-1111-111111111111')
    useProjectStore.getState().clearCurrentProjectId()

    expect(useProjectStore.getState().currentProjectId).toBeNull()
    expect(storage.getItem('cmplus.currentProjectId')).toBeNull()
  })
})
