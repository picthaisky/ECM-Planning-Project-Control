import { execSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import { installMockAuth, loginAndOpenScreen } from './support/siteOutbox'

/**
 * S13-FE-02 (`docs/10.` Sprint 13, frontend-developer row; US-13.1): real-browser, real-production-
 * build E2E for `web/src/sw.ts` — precache, the runtime-cache "ข้อมูลออฟไลน์" badge, and the
 * versioning/update-notify flow that is this task's actual substance (a service worker that never
 * updates is worse than none at all).
 *
 * Run via `npm run test:e2e:sw` — see `playwright.sw.config.ts`'s own top comment for why this is a
 * **separate** config (a real `vite build` + `vite preview`, not the rest of the suite's `vite dev`;
 * `/sw.js` and `registerServiceWorker.ts`'s own `import.meta.env.PROD` gate both require it).
 *
 * No real backend — every HTTP call this suite makes is intercepted via `page.route`/`context.route`
 * (`support/siteOutbox.ts`), same discipline as every other spec in this repo's E2E suite. Note the
 * `context.route()` used for the second test's own route below, not `page.route()` — see that test's
 * inline remarks for why a page-scoped route is not enough once the service worker is controlling.
 */

const webDir = fileURLToPath(new URL('..', import.meta.url))

/** Waits for *this* page to actually be controlled by an active service worker — the very first load
 * of a fresh browser context never is (a service worker never controls the page that triggered its
 * own registration, only subsequent navigations), so every test needs one explicit reload after the
 * first successful registration before the offline/cache behaviour below is actually in effect. */
async function ensureServiceWorkerControlsThisPage(page: Page): Promise<void> {
  await page.waitForFunction(() => navigator.serviceWorker.ready.then(() => true), undefined, { timeout: 20_000 })
  if (await page.evaluate(() => navigator.serviceWorker.controller !== null)) return
  await page.reload()
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, undefined, { timeout: 20_000 })
}

async function currentCmplusCacheNames(page: Page): Promise<string[]> {
  return page.evaluate(async () => (await caches.keys()).filter((name) => name.startsWith('cmplus-')))
}

test.describe('S13-FE-02 — service worker: precache + offline navigation', () => {
  test('a genuinely offline reload still boots the app shell, including a deep link route it was never told about explicitly', async ({
    page,
    context,
  }) => {
    // No backend mocking needed at all for this test — /login renders with zero network calls, so a
    // pass here is unambiguous proof of the precache + navigation fallback against *real* browser
    // offline emulation (context.setOffline), not a page.route illusion of it.
    await page.goto('/login')
    await expect(page.getByRole('heading', { name: 'CM+ Project Control' })).toBeVisible()
    await ensureServiceWorkerControlsThisPage(page)

    await context.setOffline(true)

    // A deep link, not just '/', proves the fallback is genuinely path-independent (the SPA shell
    // boots and react-router resolves the route client-side) — this is the exact gap
    // `e2e/photo-offline.spec.ts`'s own "no loss" test had to route around before this shipped.
    await page.goto('/app/some-project-id/wbs')
    await expect(page.getByRole('heading', { name: 'CM+ Project Control' })).toBeVisible({ timeout: 15_000 })
  })
})

test.describe('S13-FE-02 — runtime cache + "ข้อมูลออฟไลน์" badge', () => {
  test('when the live request fails, the WBS tree renders its last-known data with the offline badge, never a blank/error screen', async ({
    page,
  }) => {
    // Establish service-worker control *before* logging in, exactly like the other two tests in this
    // file — `authStore.ts` is deliberately in-memory-only (never persisted, see that file's own
    // remarks), so the reload `ensureServiceWorkerControlsThisPage` performs when the page is not yet
    // controlled would otherwise drop the session if it ran *after* `loginAndOpenScreen`, bouncing the
    // page back to `/login` and making every assertion below observe the login screen instead of the
    // WBS tree (found via a real run, not theorized: this is exactly what happened before this fix).
    await page.goto('/login')
    await expect(page.getByRole('heading', { name: 'CM+ Project Control' })).toBeVisible()
    await ensureServiceWorkerControlsThisPage(page)

    await installMockAuth(page)
    let wbsTreeRequests = 0
    // Context-level, not `page.route()`: once the page is service-worker-controlled (as it now is —
    // see above), the actual live `fetch(request)` for a `GET` this worker intercepts is issued from
    // *the service worker's own execution context* (`respondToApiGet` in `src/sw.ts`), not the page's
    // frame — `page.route()` only patches requests attributable to the page's own frame tree, so a
    // page-scoped route here would never see this request at all, and it would fall through straight
    // to the real network (observed directly: `vite preview`'s dev proxy logging a genuine
    // `ECONNREFUSED` for this exact URL). `context.route()` covers both.
    await page.context().route('**/api/v1/projects/*/wbs-tree', async (route) => {
      wbsTreeRequests += 1
      if (wbsTreeRequests === 1) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            projectId: 'fake-project',
            rootNodes: [{ id: 'root-1', parentWbsNodeId: null, code: '1', title: 'งานโครงสร้าง', weightPercentage: '38.00', activityCount: 4, children: [] }],
          }),
        })
        return
      }
      // Every later request simulates a genuine network failure (the same mechanism
      // `support/photoOffline.ts`'s own `network-drop` outcome already relies on) — this isolates
      // exactly one thing (does the runtime-cache fallback engage when *this* request fails) without
      // needing a full-browser offline/re-login dance.
      await route.abort('connectionfailed')
    })

    // The service worker already controls this page from the start now, so this first (successful)
    // request is itself served through `respondToApiGet` and populates the runtime cache for real —
    // no extra reload-to-establish-control step is needed here any more.
    await loginAndOpenScreen(page, 'WBS & Activity', 'ขยายทั้งหมด')
    await expect(page.getByText('งานโครงสร้าง')).toBeVisible()
    await expect(page.getByText('ข้อมูลออฟไลน์')).not.toBeVisible()

    // Client-side navigation away and back remounts `WbsPage` (`useWbsTree`'s own mount effect) —
    // deliberately not a full page reload, which would also drop the in-memory-only session
    // (`authStore.ts`) and conflate two different concerns.
    await page.getByRole('link', { name: 'Executive Dashboard' }).click()
    await page.getByRole('link', { name: 'WBS & Activity' }).click()

    await expect(page.getByText('ข้อมูลออฟไลน์')).toBeVisible({ timeout: 15_000 })
    // The stale-but-real data is still shown — this is graceful degradation, not an error state.
    await expect(page.getByText('งานโครงสร้าง')).toBeVisible()
  })
})

test.describe('S13-FE-02 — versioning: a new deploy does not strand an open tab on a stale cache', () => {
  test('a rebuilt deploy is detected, the update banner appears, and clicking it activates the new version while purging the old caches', async ({
    page,
  }) => {
    await page.goto('/login')
    await expect(page.getByRole('heading', { name: 'CM+ Project Control' })).toBeVisible()
    await ensureServiceWorkerControlsThisPage(page)

    const cachesBeforeDeploy = await currentCmplusCacheNames(page)
    expect(cachesBeforeDeploy.length).toBeGreaterThan(0)
    expect(cachesBeforeDeploy.every((name) => !name.includes('e2e-sw-v2'))).toBe(true)

    // Simulate a real deploy: rebuild in place while `vite preview` (this config's webServer) keeps
    // serving straight off disk, with a distinct, assertable VITE_BUILD_ID (S13-DO-01's own hook).
    execSync('npm run build', {
      cwd: webDir,
      env: { ...process.env, VITE_BUILD_ID: 'e2e-sw-v2' },
      stdio: 'pipe',
    })

    // The browser does not necessarily re-check `/sw.js` on its own within this test's timeout —
    // `registerServiceWorker.ts`'s own periodic/visibility-triggered checks are for a tab left open
    // for a long time; here the update check is triggered explicitly, exactly mirroring what those
    // triggers themselves call.
    await page.evaluate(async () => {
      const registration = await navigator.serviceWorker.getRegistration()
      await registration?.update()
    })

    await expect(page.getByRole('alert')).toContainText('มีอัปเดตใหม่ของระบบพร้อมใช้งาน', { timeout: 20_000 })

    // Not yet activated — the whole point is that an open tab is never forced onto the new version.
    const cachesWhileWaiting = await currentCmplusCacheNames(page)
    expect(cachesWhileWaiting.some((name) => name.includes('e2e-sw-v2'))).toBe(true) // precached already, in the background
    expect(cachesWhileWaiting.some((name) => !name.includes('e2e-sw-v2'))).toBe(true) // old version's caches still intact

    await page.getByRole('button', { name: 'โหลดหน้าใหม่เพื่ออัปเดต' }).click()

    // `activatePendingUpdate` -> the waiting worker skips waiting -> `controllerchange` ->
    // `registerServiceWorker.ts` reloads the page exactly once.
    await page.waitForLoadState('load')
    await expect(page.getByRole('heading', { name: 'CM+ Project Control' })).toBeVisible()

    const cachesAfterActivation = await currentCmplusCacheNames(page)
    expect(cachesAfterActivation.length).toBeGreaterThan(0)
    expect(cachesAfterActivation.every((name) => name.includes('e2e-sw-v2'))).toBe(true) // old version fully purged
  })
})
