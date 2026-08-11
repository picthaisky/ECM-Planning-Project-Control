import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ToastViewport } from './components'
import { createIndexedDbOutboxStorage, purgeExpiredSyncedItems, registerOutboxLogoutQuarantine } from './services/outbox'

// H-02 (Sprint 12 security review) fix bullet 2: registered once, here, at app startup — not inside
// any single feature — so it transparently covers every way a session can end (manual "ออกจากระบบ",
// and `apiClient.ts`'s forced logout on an expired/invalid token) and every outbox `kind`, including
// ones S13-FE-01 has not added yet. See `services/outbox/authLifecycle.ts`.
registerOutboxLogoutQuarantine()

// H-02 fix bullet 3: a device-wide, owner-agnostic retention sweep, run once per app boot alongside
// the quarantine registration above — not per-feature-hook — so this is also zero extra wiring for
// S13-FE-01's kinds. Every real session start already goes through this file (`authStore.ts`'s
// in-memory-only token model means a full reload, hence a fresh module evaluation, is required for
// every login), so boot time is a natural, regularly-hit trigger for a bounded-retention sweep.
// Best-effort; must never block first paint.
void purgeExpiredSyncedItems(createIndexedDbOutboxStorage()).catch(() => {})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
    {/* Cross-cutting (S2-FE-01): renders toasts pushed from anywhere, e.g. apiClient.ts's 401
        "session expired" notice, regardless of which screen is currently mounted. */}
    <ToastViewport />
  </StrictMode>,
)
