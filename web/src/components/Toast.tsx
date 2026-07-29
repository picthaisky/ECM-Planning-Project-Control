import { useEffect } from 'react'
import type { ToastMessage } from '../store/toastStore'
import { TOAST_AUTO_DISMISS_MS, useToastStore } from '../store/toastStore'

function ToastItem({ toast }: { toast: ToastMessage }) {
  const dismiss = useToastStore((state) => state.dismiss)

  useEffect(() => {
    const timer = window.setTimeout(() => dismiss(toast.id), TOAST_AUTO_DISMISS_MS)
    return () => window.clearTimeout(timer)
  }, [toast.id, dismiss])

  return (
    <div
      role="status"
      className="flex items-center gap-2 rounded-card bg-navy px-4 py-3 text-sm text-white shadow-lg"
    >
      <span aria-hidden="true" className="h-2 w-2 flex-shrink-0 rounded-full bg-gold" />
      <span>{toast.message}</span>
      <button
        type="button"
        aria-label="ปิดการแจ้งเตือน"
        onClick={() => dismiss(toast.id)}
        className="ml-2 text-white/70 hover:text-white"
      >
        &times;
      </button>
    </div>
  )
}

/**
 * Fixed bottom-right toast stack (`/cmplus-ui` "Toast" pattern: navy bg, white text, gold status
 * dot, 8px radius). Mount once near the app root (see `main.tsx`) — every toast pushed via
 * `pushToast`/`useToastStore.show` (e.g. `apiClient.ts`'s 401 handler) renders here regardless of
 * which screen is currently mounted. Minimal by design (S2-FE-01's DoD needs exactly one notice,
 * a session-expired message) — not a general notification system.
 */
export function ToastViewport() {
  const toasts = useToastStore((state) => state.toasts)

  if (toasts.length === 0) return null

  return (
    <div className="pointer-events-none fixed bottom-4 right-4 z-[100] flex flex-col gap-2">
      {toasts.map((toast) => (
        <div key={toast.id} className="pointer-events-auto">
          <ToastItem toast={toast} />
        </div>
      ))}
    </div>
  )
}
