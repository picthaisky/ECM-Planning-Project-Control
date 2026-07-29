import { create } from 'zustand'

export interface ToastMessage {
  id: string
  message: string
}

interface ToastState {
  toasts: ToastMessage[]
  show: (toast: Omit<ToastMessage, 'id'>) => void
  dismiss: (id: string) => void
}

/** `ToastItem` (src/components/Toast.tsx) auto-dismisses after this many ms. */
export const TOAST_AUTO_DISMISS_MS = 5000

let nextToastId = 0

/**
 * Minimal toast queue (Zustand — UI state only). Scoped to exactly what S2-FE-01's DoD needs (a
 * 401 "session expired" notice from `apiClient.ts`) — not a general notification system.
 * `ToastViewport` (src/components/Toast.tsx) renders this queue; mount it once near the app root.
 */
export const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  show: (toast) =>
    set((state) => ({
      toasts: [...state.toasts, { ...toast, id: `toast-${(nextToastId += 1)}` }],
    })),
  dismiss: (id) => set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) })),
}))

/** Non-hook helper for use outside a React component (e.g. `apiClient.ts`'s response interceptor,
 * which runs outside the render tree). */
export function pushToast(toast: Omit<ToastMessage, 'id'>): void {
  useToastStore.getState().show(toast)
}
