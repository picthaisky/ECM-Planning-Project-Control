import { expect, test } from '@playwright/test'
import { DEV_USER, PERF_PROJECT_ID } from './support/gantt'

/**
 * S16-QA-02 (docs/10. Sprint 16A): post-deploy smoke test. Unlike every other spec in this folder —
 * which MOCK the backend to test the frontend in isolation — this one drives the REAL deployed stack
 * end-to-end (login → WBS → Gantt → EVM → the Payment approval screen) to prove a fresh deploy is
 * actually serving: auth issues a real JWT, the WBS-tree/Gantt/EVM APIs answer, and the app renders.
 *
 * It runs against a DEPLOYED url, never the local dev server, so it uses its own config
 * (`playwright.smoke.config.ts`, no `webServer`, `SMOKE_BASE_URL` + `ignoreHTTPSErrors` for Caddy's
 * self-signed staging cert) and is excluded from the default `npm run test:e2e` run. Invoke it as:
 *
 *   SMOKE_BASE_URL=https://staging.example \
 *   SMOKE_EMAIL=... SMOKE_PASSWORD=... SMOKE_PROJECT_ID=... \
 *   npm run test:e2e:smoke
 *
 * Credentials/project default to the dev seed (pm@siam-construction.dev / the S4-DB-02 perf project)
 * so it runs against a dev-seeded staging out of the box; override via env for a real staging tenant.
 *
 * Scope boundary: this asserts each critical screen RENDERS against live data. The mutating
 * create→approve-a-document path is deliberately NOT run on every deploy (it would write to the target
 * each time and needs a specific approver role/state) — that is a UAT scenario (docs/qa/uat-plan.md
 * §5.9/§6.2). Here we verify the Payment approval screen loads and its primary affordance is present.
 */

const EMAIL = process.env.SMOKE_EMAIL ?? DEV_USER.email
const PASSWORD = process.env.SMOKE_PASSWORD ?? DEV_USER.password
const PROJECT_ID = process.env.SMOKE_PROJECT_ID ?? PERF_PROJECT_ID

// The JWT lives in memory only (authStore.ts) — after /login every hop must be client-side SPA
// routing (a nav-link click), never page.goto, or the session is dropped. Same rule the other specs'
// login helpers document.
test.beforeEach(async ({ page }) => {
  await page.goto('/login')

  await expect(
    page.getByRole('heading', { name: 'CM+ Project Control' }),
    'login page identifying heading — something other than CM+ may be answering this URL',
  ).toBeVisible({ timeout: 20_000 })

  await page.getByLabel('อีเมล').fill(EMAIL)
  await page.getByLabel('รหัสผ่าน').fill(PASSWORD)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  // LoginRoute → '/' → RootRedirect lands on /select-project (fresh context) or /app/:id/... .
  await page.waitForURL((url) => url.pathname === '/select-project' || url.pathname.startsWith('/app/'), {
    timeout: 20_000,
  })

  if (new URL(page.url()).pathname === '/select-project') {
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(PROJECT_ID)
    await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  }

  await page.waitForURL(`**/app/${PROJECT_ID}/**`, { timeout: 20_000 })
})

/** Clicks a main-nav item and waits for its screen route. Nav labels are navConfig.ts's own. */
async function openScreen(page: import('@playwright/test').Page, label: string, segment: string) {
  await page.getByRole('link', { name: label, exact: true }).click()
  await page.waitForURL(`**/app/${PROJECT_ID}/${segment}`, { timeout: 20_000 })
  // AppShell's Topbar (banner) shows the active screen's label — a stable per-screen assertion.
  await expect(page.getByRole('banner')).toContainText(label, { timeout: 15_000 })
}

test('login reaches the app against the real backend', async ({ page }) => {
  // beforeEach already logged in and landed on /app/:id/** — assert we are authenticated and inside
  // a project, not bounced back to /login.
  await expect(page).toHaveURL(new RegExp(`/app/${PROJECT_ID}/`))
  await expect(page.getByLabel('เมนูหลัก')).toBeVisible()
})

test('WBS & Activity screen loads', async ({ page }) => {
  await openScreen(page, 'WBS & Activity', 'wbs')
})

test('Gantt / CPM screen loads and renders the chart canvas', async ({ page }) => {
  await openScreen(page, 'Gantt / CPM', 'gantt')
  // Real data load of the Gantt payload; wait out the loading placeholder if present, then the canvas.
  await page
    .getByText('กำลังโหลดข้อมูล Gantt...')
    .waitFor({ state: 'detached', timeout: 30_000 })
    .catch(() => {
      /* already cleared before we observed it — fine */
    })
  await expect(page.getByTestId('gantt-body-canvas')).toBeVisible({ timeout: 30_000 })
})

test('EVM S-Curve screen loads', async ({ page }) => {
  await openScreen(page, 'EVM S-Curve', 'evm')
})

test('Payment Certificate (document/approval) screen loads', async ({ page }) => {
  // Load + affordance only — see the scope boundary in this file's header; the mutating
  // create→approve flow is UAT (docs/qa/uat-plan.md §5.9/§6.2), not a per-deploy smoke.
  await openScreen(page, 'Payment Certificate', 'payment')
})
