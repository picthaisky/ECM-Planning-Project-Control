import { useAuthStore } from '../store/authStore'
import { useSwUpdateStore } from '../store/swUpdateStore'

/** How often an already-open tab re-checks for a new deploy, beyond whatever automatic checks the
 * browser itself performs on navigation — a site tablet realistically stays on one screen for a
 * whole shift, so relying only on navigation-triggered checks could leave it on a stale build for
 * days (S13-FE-02's core "must not strand clients" requirement). */
const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000

/** Guards the `controllerchange` reload so a browser that (rarely, but per spec, legally) fires it
 * more than once only ever reloads this page once. */
let hasReloadedForUpdate = false

/**
 * True only once the user has explicitly clicked "โหลดหน้าใหม่เพื่ออัปเดต" (`activatePendingUpdate`
 * below). Load-bearing: `self.clients.claim()` in `src/sw.ts#activate` — needed so a worker that
 * finishes activating *while a page is already open* actually takes it over — also fires
 * `controllerchange` on this browser's very first-ever visit (an uncontrolled page gaining its first
 * controller is indistinguishable, at the event level, from an existing controller being replaced by
 * a new version). Without this guard every first-time visitor would see a surprise, unrequested full
 * page reload moments after the page finished loading — found via a real Playwright run racing this
 * exact reload against the test's own navigation, not merely theorized.
 */
let updateActivationRequested = false

function trackInstallingWorker(worker: ServiceWorker): void {
  worker.addEventListener('statechange', () => {
    // `installed` + an existing controller means this is an *update* to an already-running app, not
    // this browser's very first install of it (where there is no controller yet, and — correctly —
    // nothing to warn anyone about; the page they are looking at already IS the new version).
    if (worker.state === 'installed' && navigator.serviceWorker.controller) {
      useSwUpdateStore.getState().setUpdateAvailable(true)
    }
  })
}

function watchForUpdates(registration: ServiceWorkerRegistration): void {
  // A worker can already be `installing` by the time this line runs (registration resolved just now,
  // racing `updatefound`) — cover both that and every future `updatefound` on this same registration.
  if (registration.installing) trackInstallingWorker(registration.installing)
  registration.addEventListener('updatefound', () => {
    if (registration.installing) trackInstallingWorker(registration.installing)
  })
}

/**
 * S13-FE-02: registers `dist/sw.js` (production builds only — see `src/sw.ts`'s own module comment
 * for the full precache/versioning/update-flow design this wires up) and the two logout-time
 * cleanup hooks: `controllerchange` -> reload once the user has consented to the new version
 * (`UpdateAvailableBanner.tsx` -> `activatePendingUpdate` below), and an auth-store subscription that
 * clears the *runtime* cache (never the precache, which holds no user data) on every logout —
 * `services/outbox/authLifecycle.ts#registerOutboxLogoutQuarantine`'s sibling for cached API
 * responses, so a second person signing in on a shared site tablet can never read the previous
 * person's last-cached table data while offline.
 *
 * Call exactly once, from `main.tsx`, mirroring `registerOutboxLogoutQuarantine`'s own call-once
 * convention.
 */
export function registerServiceWorker(): void {
  if (!('serviceWorker' in navigator)) return
  // No service worker in dev/test — Vite's own dev server already serves fresh modules on every
  // request, and a worker registered against `npm run dev` would fight the dev server's own HMR/
  // module graph rather than helping it. `sw.js` also does not exist as a route until `vite build`
  // has run `vite.config.ts`'s `serviceWorkerPlugin`.
  if (!import.meta.env.PROD) return

  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!updateActivationRequested || hasReloadedForUpdate) return
    hasReloadedForUpdate = true
    window.location.reload()
  })

  navigator.serviceWorker
    .register('/sw.js')
    .then((registration) => {
      watchForUpdates(registration)
      window.setInterval(() => void registration.update(), UPDATE_CHECK_INTERVAL_MS)
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') void registration.update()
      })
    })
    .catch(() => {
      // Best-effort — a registration failure (e.g. an environment serving over plain HTTP, where
      // service workers are refused outside `localhost`) must never block the app itself from
      // working; it just means this tab runs without offline/precache support.
    })

  useAuthStore.subscribe((state, previousState) => {
    const justLoggedOut = previousState.isAuthenticated && !state.isAuthenticated
    if (!justLoggedOut) return
    navigator.serviceWorker.controller?.postMessage({ type: 'CLEAR_RUNTIME_CACHE' })
  })
}

/** `UpdateAvailableBanner.tsx`'s "โหลดหน้าใหม่เพื่ออัปเดต" action — tells the *waiting* worker
 * (installed by `watchForUpdates` above, holding until now on purpose — see `src/sw.ts`'s own remarks
 * on why `install` never calls `skipWaiting()` itself) to take over. `registerServiceWorker`'s own
 * `controllerchange` listener performs the actual reload once that handover completes. */
export function activatePendingUpdate(): void {
  updateActivationRequested = true
  useSwUpdateStore.getState().setUpdateAvailable(false)
  void navigator.serviceWorker.getRegistration().then((registration) => {
    registration?.waiting?.postMessage({ type: 'SKIP_WAITING' })
  })
}
