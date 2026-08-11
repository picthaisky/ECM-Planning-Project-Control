import { activatePendingUpdate } from '../services/registerServiceWorker'
import { useSwUpdateStore } from '../store/swUpdateStore'

/**
 * S13-FE-02's "stale-cache trap" fix, made visible: a persistent (never auto-dismissing, unlike
 * `Toast.tsx`) top banner shown the moment a new deploy's service worker has finished installing
 * (`services/registerServiceWorker.ts`) — pinned to the top so it never collides with the
 * bottom-right toast stack. Mount once near the app root (`main.tsx`), alongside `ToastViewport`.
 */
export function UpdateAvailableBanner() {
  const updateAvailable = useSwUpdateStore((state) => state.updateAvailable)

  if (!updateAvailable) return null

  return (
    <div
      role="alert"
      className="fixed inset-x-0 top-0 z-[100] flex items-center justify-center gap-3 bg-navy px-4 py-2.5 text-[12.5px] text-white shadow-lg"
    >
      <span aria-hidden="true" className="h-2 w-2 flex-shrink-0 rounded-full bg-gold" />
      <span>มีอัปเดตใหม่ของระบบพร้อมใช้งาน</span>
      <button
        type="button"
        onClick={activatePendingUpdate}
        className="rounded-md bg-gold px-3 py-1 font-semibold text-navy hover:bg-gold/90"
      >
        โหลดหน้าใหม่เพื่ออัปเดต
      </button>
    </div>
  )
}
