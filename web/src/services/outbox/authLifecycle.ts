import { useAuthStore } from '../../store/authStore'
import { createIndexedDbOutboxStorage } from './storage'
import { quarantineOwnerBlobs } from './outboxMaintenance'

/**
 * Wires the logout-time blob quarantine (H-02 fix bullet 2, Sprint 12 security review) to the shared
 * auth store, once, for the whole app's lifetime. Call this exactly once at app startup
 * (`main.tsx`) — never from an individual feature — so S13-FE-01's weather-log/progress-batch kinds
 * are covered by the exact same registration with zero changes here: `quarantineOwnerBlobs` already
 * sweeps every `kind` in the one shared IndexedDB database.
 *
 * Subscribes directly to `useAuthStore` (rather than being called from each `logout()` call site)
 * so it transparently covers *every* way a session ends today — the manual "ออกจากระบบ" button
 * (`Sidebar.tsx`), the auto-logout `LoginPage.tsx` screen's own button, and `apiClient.ts`'s forced
 * logout on an expired/invalid token (401) — without any of those call sites needing to know the
 * outbox exists.
 *
 * Idempotent: a second call is a safe no-op, guarding against React `StrictMode`/HMR re-invoking
 * module-level setup twice in development.
 */
let registered = false

export function registerOutboxLogoutQuarantine(): void {
  if (registered) return
  registered = true

  useAuthStore.subscribe((state, previousState) => {
    const justLoggedOut = previousState.isAuthenticated && !state.isAuthenticated
    if (!justLoggedOut || !previousState.claims) return

    const owner = { userId: previousState.claims.userId, tenantId: previousState.claims.tenantId }
    // Best-effort: a quarantine failure (e.g. IndexedDB unavailable in a locked-down browser mode)
    // must never block the logout/navigation flow that triggered it.
    void quarantineOwnerBlobs(createIndexedDbOutboxStorage(), owner).catch(() => {})
  })
}
