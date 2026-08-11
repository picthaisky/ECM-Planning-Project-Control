/**
 * S13-FE-02 (US-13.1, ADR-0005): CM+'s service worker — precache (the app shell: JS/CSS/fonts/
 * `index.html`), a runtime cache (API `GET` responses, so a table already viewed once stays visible
 * offline), and a versioning/update scheme designed specifically to avoid the "stale cache trap": a
 * service worker that silently keeps serving an old build forever is worse than shipping none at
 * all, because a normal page reload no longer even reaches the network to notice a new deploy.
 *
 * ## Versioning scheme (read this before changing `S13-DO-01`'s CI wiring)
 *
 * `__CM_SW_VERSION__` and `__CM_PRECACHE_URLS__` are compile-time constants substituted by
 * `vite.config.ts`'s `swBuildPlugin` (via esbuild's own `define`, not a runtime lookup) when this
 * file is built as a **separate** artifact (`dist/sw.js`) from the main app bundle — see that
 * plugin's own comment for exactly how. `__CM_SW_VERSION__` is `process.env.VITE_BUILD_ID` if the
 * build set it (CI should set this to the deploy's immutable identifier, e.g. the git commit SHA:
 * `VITE_BUILD_ID=$(git rev-parse HEAD)`), else a `dev-<timestamp>` fallback so even an ad hoc local
 * `npm run build` gets a fresh, distinct version every time (useful for manually testing the update
 * flow below). **Every cache name this file opens is derived from that one version string** —
 * `activate` deletes any `cmplus-*` cache that is not one of the two current-version names, so a new
 * deploy's very first activation cleans up everything the previous version left behind. This is the
 * whole mechanism `S13-DO-01` needs to build on: set `VITE_BUILD_ID` in CI before `npm run build` and
 * versioning/cache-busting/stale-cache-cleanup all follow automatically from it — no other code
 * change is required on the deploy pipeline's side.
 *
 * ## Update flow (why this never strands a client on a stale cache, and never yanks the rug either)
 *
 * A new service worker `install`s and precaches the *new* version's assets into a *new*-named cache
 * as soon as the browser fetches an updated `sw.js` (browsers check for this on navigation and
 * periodically) — but it deliberately does **not** call `self.skipWaiting()` during `install`, so an
 * already-open tab keeps running its current (old) version, uninterrupted, until a human says so.
 * `main.tsx`'s `registerServiceWorker.ts` detects this "installed, waiting" state and shows a
 * persistent "มีอัปเดตใหม่ กดเพื่อโหลดหน้าใหม่" banner (`UpdateAvailableBanner.tsx`); only when the user
 * clicks it does the page `postMessage({type:'SKIP_WAITING'})` to the waiting worker, which is what
 * this file's own `message` handler below acts on. The moment the new worker actually takes control
 * (`controllerchange`), the registration code reloads the page exactly once. Net effect: a tab left
 * open for days is *told* about an update instead of silently drifting from what a fresh load would
 * serve, but is never forcibly reloaded mid-use.
 *
 * ## What the runtime cache never does
 *
 * Only `GET` requests are ever intercepted — every outbox write (`services/outbox/`) goes straight to
 * the network exactly as before, so this file has zero interaction with ADR-0005's write path. And
 * because an API GET response can be tenant/user-specific, `registerServiceWorker.ts` clears the
 * runtime cache (not the precache — the app shell is not user-specific) on every logout, mirroring
 * `services/outbox/authLifecycle.ts`'s own logout-quarantine pattern; without that, a second person
 * signing in on the same shared site tablet could otherwise be served the *previous* person's last
 * cached API response while offline — the same shared-device class of risk the Sprint 12 security
 * review's H-02 finding was about, closed here the same way: scope it to the session, and clear it on
 * handoff.
 */

declare const __CM_SW_VERSION__: string
declare const __CM_PRECACHE_URLS__: readonly string[]
declare const self: ServiceWorkerGlobalScope

const SW_VERSION = __CM_SW_VERSION__
const PRECACHE_URLS: readonly string[] = __CM_PRECACHE_URLS__

const CACHE_NAME_PREFIX = 'cmplus-'
const PRECACHE_NAME = `${CACHE_NAME_PREFIX}precache-${SW_VERSION}`
const RUNTIME_CACHE_NAME = `${CACHE_NAME_PREFIX}runtime-${SW_VERSION}`
const CURRENT_CACHE_NAMES = new Set([PRECACHE_NAME, RUNTIME_CACHE_NAME])

/** The one signal `apiClient.ts#isServedFromOfflineCache` looks for — set only on a response this
 * worker actually served from `RUNTIME_CACHE_NAME` because the live fetch failed. */
const SERVED_FROM_CACHE_HEADER = 'X-Cm-Served-From'
const SERVED_FROM_CACHE_VALUE = 'sw-cache'

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(PRECACHE_NAME)
      // `addAll` fails the whole install if any single asset 404s — deliberate: a partial precache
      // covering only *some* of this build's own JS/CSS would falsely promise offline support while
      // consistently 404ing on exactly the assets this build actually depends on.
      await cache.addAll([...PRECACHE_URLS])
    })(),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const existingCacheNames = await caches.keys()
      await Promise.all(
        existingCacheNames
          .filter((name) => name.startsWith(CACHE_NAME_PREFIX) && !CURRENT_CACHE_NAMES.has(name))
          .map((name) => caches.delete(name)),
      )
      await self.clients.claim()
    })(),
  )
})

self.addEventListener('message', (event) => {
  const data = event.data as { type?: string } | undefined
  if (data?.type === 'SKIP_WAITING') {
    void self.skipWaiting()
    return
  }
  // `registerServiceWorker.ts`'s logout hook — see this file's own module comment on why the
  // runtime cache (never the precache, which holds no user data) must not survive a session handoff
  // on a shared device.
  if (data?.type === 'CLEAR_RUNTIME_CACHE') {
    event.waitUntil(caches.delete(RUNTIME_CACHE_NAME))
  }
})

function isNavigationRequest(request: Request): boolean {
  return request.mode === 'navigate'
}

function isApiGetRequest(request: Request, url: URL): boolean {
  return request.method === 'GET' && url.pathname.startsWith('/api/')
}

/** Navigation (HTML page load) requests: network-first, falling back to the precached app shell so a
 * genuinely offline reload — including a deep link like `/app/:id/wbs`, which react-router resolves
 * client-side once the shell has booted — still boots the app instead of the browser's bare
 * "no internet" page (the exact gap `e2e/photo-offline.spec.ts` had to work around before this
 * shipped — see that spec's own remarks). */
async function respondToNavigation(request: Request): Promise<Response> {
  try {
    return await fetch(request)
  } catch {
    const cache = await caches.open(PRECACHE_NAME)
    // `ignoreVary: true` — see `respondFromPrecache`'s own remarks on why matching these immutable,
    // content-hashed assets must not depend on `Vary`.
    const shell = (await cache.match('/index.html', { ignoreVary: true })) ?? (await cache.match('/', { ignoreVary: true }))
    if (shell) return shell
    throw new Error('CM+ offline: no cached app shell available for this device yet')
  }
}

/**
 * Precached static assets (this build's own JS/CSS/fonts/icon): cache-first — they are immutable
 * per version (Vite's own content hash in the filename), so there is nothing to revalidate.
 *
 * `ignoreVary: true` is load-bearing, found via a real Playwright run, not theorized: Vite emits
 * `<script crossorigin src="...">`/`<link crossorigin href="...">`, so the browser's *real* request
 * for these assets carries an `Origin` header a same-origin request normally would not — if the
 * server response for that asset carries `Vary: Origin` (common for a static file server that also
 * answers CORS-mode requests), the Cache API's *default* matching (`ignoreVary: false`) then requires
 * an exact `Origin` match against whatever request `cache.addAll` happened to store during `install`
 * (issued from the service worker's own context, not a page's `crossorigin` script tag) — a mismatch
 * that makes `cache.match()` silently miss an entry that `cache.keys()` plainly shows is present.
 * These files are immutable per version by construction (the content hash *is* the cache-busting
 * mechanism), so nothing is lost by ignoring `Vary` here — unlike `respondToApiGet`, which must not.
 */
async function respondFromPrecache(request: Request): Promise<Response> {
  const cache = await caches.open(PRECACHE_NAME)
  const cached = await cache.match(request, { ignoreVary: true })
  if (cached) return cached
  return fetch(request)
}

/** API `GET`s: network-first, and only ever falls back to the runtime cache when the live request
 * itself fails — so cached data is shown exactly when, and only when, it is genuinely the best
 * available answer. A successful response is cached for next time; a non-2xx response is passed
 * through untouched and never cached (an error page must never be replayed later as if it were data).
 * The fallback response is stamped with `SERVED_FROM_CACHE_HEADER` so `apiClient.ts` can tell the two
 * cases apart (`services/useOutboxSyncStatus.ts`/`features/wbs/useWbsTree.ts`'s "ข้อมูลออฟไลน์" badge). */
async function respondToApiGet(request: Request): Promise<Response> {
  const cache = await caches.open(RUNTIME_CACHE_NAME)
  try {
    const response = await fetch(request)
    if (response.ok) {
      await cache.put(request, response.clone())
    }
    return response
  } catch {
    const cached = await cache.match(request)
    if (!cached) throw new Error('CM+ offline: no cached response for this request yet')
    const headers = new Headers(cached.headers)
    headers.set(SERVED_FROM_CACHE_HEADER, SERVED_FROM_CACHE_VALUE)
    return new Response(cached.body, { status: cached.status, statusText: cached.statusText, headers })
  }
}

self.addEventListener('fetch', (event) => {
  const { request } = event
  // Never intercept writes — every outbox upload (ADR-0005) must reach the real network (or fail
  // honestly) exactly as if this worker were not installed at all.
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return // cross-origin requests are left entirely alone

  if (isNavigationRequest(request)) {
    event.respondWith(respondToNavigation(request))
    return
  }
  if (isApiGetRequest(request, url)) {
    event.respondWith(respondToApiGet(request))
    return
  }
  if (PRECACHE_URLS.includes(url.pathname)) {
    event.respondWith(respondFromPrecache(request))
  }
  // Anything else (a static asset from a build this worker does not recognise, e.g. before its own
  // first activation) is left to the network, uninvolved — no blanket "cache everything" behaviour.
})
