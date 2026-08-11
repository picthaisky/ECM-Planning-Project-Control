import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { detectSyncCapability, registerBackgroundSyncIfSupported, registerOutboxSyncTriggers } from './syncTriggers'

/** jsdom implements neither `navigator.serviceWorker` nor `window.SyncManager` — both are defined
 * fresh per test (when needed) and unconditionally torn down here, so no test's override can leak
 * into a later test regardless of run order. */
function resetSyncGlobals(): void {
  // @ts-expect-error -- test-only cleanup of a jsdom global neither type declares
  delete window.SyncManager
  // @ts-expect-error -- test-only cleanup of a jsdom global neither type declares
  delete navigator.serviceWorker
}

describe('detectSyncCapability', () => {
  afterEach(resetSyncGlobals)

  it('reports fallback-only on a browser with neither serviceWorker nor SyncManager (e.g. Safari/iOS)', () => {
    expect(detectSyncCapability()).toBe('fallback-only')
  })

  it('reports fallback-only when serviceWorker exists but SyncManager does not (Safari has this shape)', () => {
    Object.defineProperty(navigator, 'serviceWorker', { value: {}, configurable: true })
    expect(detectSyncCapability()).toBe('fallback-only')
  })

  it('reports background-sync only when both serviceWorker and SyncManager are present', () => {
    Object.defineProperty(navigator, 'serviceWorker', { value: {}, configurable: true })
    // @ts-expect-error -- test-only global for feature detection
    window.SyncManager = function SyncManager() {}
    expect(detectSyncCapability()).toBe('background-sync')
  })
})

describe('registerBackgroundSyncIfSupported', () => {
  afterEach(resetSyncGlobals)

  it('never throws and never hangs when Background Sync is unsupported (the common case this sprint)', async () => {
    await expect(registerBackgroundSyncIfSupported()).resolves.toBeUndefined()
  })

  it('resolves quickly even when a service worker is present but getRegistration() returns nothing yet', async () => {
    // @ts-expect-error -- test-only global
    window.SyncManager = function SyncManager() {}
    Object.defineProperty(navigator, 'serviceWorker', {
      value: { getRegistration: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    })

    await expect(registerBackgroundSyncIfSupported()).resolves.toBeUndefined()
  })
})

describe('registerOutboxSyncTriggers', () => {
  let onLineSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    onLineSpy = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
  })

  afterEach(() => {
    onLineSpy.mockRestore()
    resetSyncGlobals()
  })

  it('calls onDue immediately on mount when the device is already online', () => {
    onLineSpy.mockReturnValue(true)
    const onDue = vi.fn()

    const handle = registerOutboxSyncTriggers(onDue)

    expect(onDue).toHaveBeenCalledTimes(1)
    handle.dispose()
  })

  it('does not call onDue on mount when the device is offline', () => {
    const onDue = vi.fn()
    const handle = registerOutboxSyncTriggers(onDue)
    expect(onDue).not.toHaveBeenCalled()
    handle.dispose()
  })

  it('calls onDue when an "online" event fires', () => {
    const onDue = vi.fn()
    const handle = registerOutboxSyncTriggers(onDue)
    onDue.mockClear()

    window.dispatchEvent(new Event('online'))

    expect(onDue).toHaveBeenCalledTimes(1)
    handle.dispose()
  })

  it('calls onDue when the app returns to the foreground (visibilitychange -> visible)', () => {
    const onDue = vi.fn()
    const handle = registerOutboxSyncTriggers(onDue)
    onDue.mockClear()

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
    document.dispatchEvent(new Event('visibilitychange'))

    expect(onDue).toHaveBeenCalledTimes(1)
    handle.dispose()
  })

  it('does not call onDue when visibilitychange fires while backgrounded (hidden)', () => {
    const onDue = vi.fn()
    const handle = registerOutboxSyncTriggers(onDue)
    onDue.mockClear()

    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true })
    document.dispatchEvent(new Event('visibilitychange'))

    expect(onDue).not.toHaveBeenCalled()
    handle.dispose()
  })

  it('dispose() removes listeners so a later online event no longer calls onDue', () => {
    const onDue = vi.fn()
    const handle = registerOutboxSyncTriggers(onDue)
    onDue.mockClear()
    handle.dispose()

    window.dispatchEvent(new Event('online'))

    expect(onDue).not.toHaveBeenCalled()
  })
})
