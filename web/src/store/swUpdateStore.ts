import { create } from 'zustand'

interface SwUpdateState {
  /** True once `services/registerServiceWorker.ts` has detected a *new* service worker sitting
   * `waiting` (installed, but not yet controlling this page) — i.e. a deploy happened while this tab
   * was open. `UpdateAvailableBanner.tsx` renders on this alone. */
  updateAvailable: boolean
  setUpdateAvailable: (value: boolean) => void
}

/**
 * S13-FE-02: minimal UI-state store (Zustand, mirrors `store/toastStore.ts`'s own scoped-to-exactly-
 * one-DoD shape) for the service worker's "a new version is available" signal. Deliberately a
 * persistent flag rather than an auto-dismissing toast (`toastStore.ts`'s `TOAST_AUTO_DISMISS_MS`)
 * — an update notice that quietly disappears after 5 seconds defeats the entire point of warning a
 * long-lived open tab that it is running a stale build.
 */
export const useSwUpdateStore = create<SwUpdateState>((set) => ({
  updateAvailable: false,
  setUpdateAvailable: (value) => set({ updateAvailable: value }),
}))
