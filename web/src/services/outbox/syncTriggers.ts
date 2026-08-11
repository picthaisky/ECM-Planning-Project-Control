/**
 * What actually flushes the outbox (S12-FE-01 task brief: "Background Sync isn't universally
 * available (notably Safari/iOS, ... a real construction-site device). Have a fallback — retry on
 * `online` events / app foreground — and don't let the UI claim 'will sync automatically' where that
 * isn't true.").
 *
 * Three triggers, registered together by `registerOutboxSyncTriggers`:
 * 1. **`online` event** — the device regains connectivity while the tab is open.
 * 2. **App foreground** (`visibilitychange` -> `'visible'`) — the device *was* online but the tab was
 *    backgrounded/the phone was locked when connectivity returned, so the `online` event fired with
 *    no listener alive to hear it; returning to the app is the moment to check again.
 * 3. **Mount / initial `navigator.onLine`** — the outbox may already hold pending items from a
 *    previous, now-closed session; checking once at startup means "I have signal, wait for a
 *    background trigger" is never the answer.
 *
 * A real Background Sync registration (`registerBackgroundSyncIfSupported`) is attempted too, but it
 * is deliberately best-effort and non-blocking: **this sprint ships no service worker at all**
 * (S13-FE-02 is the task that adds one) — `navigator.serviceWorker.getRegistration()` resolves to
 * `undefined` with nothing controlling the page yet, so the registration call below is a no-op today
 * and the three triggers above are what actually keeps the DoD's "เน็ตกลับมา → ... อัปโหลดเองและขึ้น
 * 'ซิงค์แล้ว'" true in the meantime. `getRegistration()` (not `.ready`, which never resolves while no
 * service worker is ever registered — a real hang this module must not introduce) is used precisely
 * so this stays a fast, always-settling check rather than a dangling promise.
 */

export type SyncCapability = 'background-sync' | 'fallback-only'

/** Feature-detects real, OS-level Background Sync support — `false` on Safari/iOS (no `SyncManager`
 * at all, any version) and on any browser with no active service worker yet. Drives the UI copy: a
 * user must never be told "will sync automatically in the background" on a device where that is not
 * true (task brief's explicit instruction). */
export function detectSyncCapability(): SyncCapability {
  const hasServiceWorker = typeof navigator !== 'undefined' && 'serviceWorker' in navigator
  const hasSyncManager = typeof window !== 'undefined' && 'SyncManager' in window
  return hasServiceWorker && hasSyncManager ? 'background-sync' : 'fallback-only'
}

/** Best-effort, never throws, never hangs (see this file's own remarks on why `getRegistration()`
 * and not `.ready`). A no-op today (no service worker ships until S13-FE-02); kept here so that task
 * only has to *implement* the `sync` event handler in the worker and call this — the registration
 * call site does not move. */
export async function registerBackgroundSyncIfSupported(tag = 'cmplus-outbox-sync'): Promise<void> {
  if (detectSyncCapability() !== 'background-sync') return

  try {
    const registration = await navigator.serviceWorker.getRegistration()
    const syncRegistration = registration as (ServiceWorkerRegistration & { sync?: { register(tag: string): Promise<void> } }) | undefined
    await syncRegistration?.sync?.register(tag)
  } catch {
    // Best-effort — the fallback triggers below are the path this sprint actually relies on.
  }
}

export interface OutboxSyncTriggersHandle {
  /** Removes every listener registered by `registerOutboxSyncTriggers`. Call from a `useEffect`
   * cleanup so a page that mounts/unmounts the outbox UI repeatedly never accumulates listeners. */
  dispose: () => void
}

/** Registers the fallback triggers described above and attempts Background Sync registration once.
 * `onDue` is called with no arguments whenever a trigger fires — the caller (`usePhotoOutbox.ts`)
 * owns debouncing/in-flight-guarding (`syncEngine.ts`'s own `isSyncing` already makes a second
 * concurrent `flush()` a safe no-op, so no additional guard is required here). */
export function registerOutboxSyncTriggers(onDue: () => void): OutboxSyncTriggersHandle {
  if (typeof window === 'undefined') {
    return { dispose: () => {} }
  }

  const handleVisibilityChange = () => {
    if (document.visibilityState === 'visible') onDue()
  }

  window.addEventListener('online', onDue)
  document.addEventListener('visibilitychange', handleVisibilityChange)
  void registerBackgroundSyncIfSupported()

  if (typeof navigator !== 'undefined' && navigator.onLine) {
    onDue()
  }

  return {
    dispose: () => {
      window.removeEventListener('online', onDue)
      document.removeEventListener('visibilitychange', handleVisibilityChange)
    },
  }
}
