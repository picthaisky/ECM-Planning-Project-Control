import type { Page } from '@playwright/test'

/**
 * Shared E2E fixtures for S6-QA-01/02 — the real S1-DB-03 dev seed user and the real S4-DB-02
 * 10,000-activity/5,000-WBS-node deterministic dataset (`tests/perf/seed-large-project.sql`),
 * both already present in the running `docker-mssql-1` volume (verified directly against
 * `GET /api/v1/projects/{id}/gantt` before writing these specs — see this sprint's QA report).
 */
export const DEV_USER = {
  email: 'pm@siam-construction.dev',
  password: 'Dev@CMPlus2026!',
}

/** Deterministic project id for the `CMPLUS-S4-DB-02-v1` seed (10,000 activities, 5,000 WBS
 * nodes, 15,000 relations) — same project `docs/perf/baseline.md` and
 * `docs/perf/gantt-frontend-s6.md` both target. */
export const PERF_PROJECT_ID = '2193e79c-a4c8-e7a5-0ffe-fb3f581af585'

/**
 * Logs in as the real dev PM user against the real running backend (via Vite's `/api` dev proxy,
 * see `playwright.config.ts`'s remarks), then navigates to the Gantt screen for `projectId` using
 * real UI interactions only (never `page.goto` for the post-login hop) — `authStore.ts` keeps the
 * JWT in memory only, deliberately never persisted, so a full page navigation would silently drop
 * the session; every hop after `/login` must be client-side SPA routing (form submit / link click)
 * to stay authenticated.
 */
export async function loginAndOpenGantt(page: Page, projectId: string): Promise<void> {
  await page.goto('/login')

  // Assert we are actually looking at CM+ before touching any field. `playwright.config.ts` now
  // pins a CM+-specific port with `reuseExistingServer: false`, which should make this impossible
  // — but this guard is what catches the failure *class*, not just the one instance: an E2E suite
  // silently driving the wrong application is the kind of thing that yields confidently-wrong
  // results. (It happened for real: Vite's default port 5173 was already serving an unrelated app,
  // Playwright reused it, and the run drove that app's login form instead of ours.) A cheap,
  // explicit identity check beats trusting that no other dev server can ever answer this port.
  await page
    .getByRole('heading', { name: 'CM+ Project Control' })
    .waitFor({ state: 'visible', timeout: 15_000 })
    .catch(() => {
      throw new Error(
        `Expected the CM+ login page at ${page.url()}, but its identifying heading was never found. ` +
          'Something other than this app is almost certainly answering that port — check for another ' +
          'dev server, or set E2E_PORT to a free port.',
      )
    })

  await page.getByLabel('อีเมล').fill(DEV_USER.email)
  await page.getByLabel('รหัสผ่าน').fill(DEV_USER.password)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  // `LoginRoute` navigates to `/` first, then `RootRedirect` immediately client-side-redirects
  // again to either `/select-project` or `/app/:projectId/...` — waiting for "not /login anymore"
  // races with that second hop and can resolve on the transient `/` URL before it lands, which was
  // observed directly (see this sprint's QA report) to cause the branch below to be silently
  // skipped. Wait for one of the two actual terminal destinations instead — this predicate can
  // never match the fleeting `/` state.
  await page.waitForURL((url) => url.pathname === '/select-project' || url.pathname.startsWith('/app/'), {
    timeout: 15_000,
  })

  // `RootRedirect` sends an authenticated user with no remembered project to `/select-project`
  // (`projectStore.ts`'s interim seam) — a fresh Playwright context has no localStorage yet, so
  // this branch is taken on effectively every run, but the check keeps the helper correct even if
  // a future test reuses `storageState`.
  if (new URL(page.url()).pathname === '/select-project') {
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(projectId)
    await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  }

  await page.waitForURL(`**/app/${projectId}/**`, { timeout: 15_000 })
  await page.getByRole('link', { name: 'Gantt / CPM' }).click()
  await page.waitForURL(`**/app/${projectId}/gantt`)

  // Real network load of the 10,000-activity payload (~3.3 MB, ~100-300ms per this sprint's own
  // measurements) + React building 10,000 row view-models — wait for the loading placeholder
  // (`GanttPage.tsx`) to clear rather than a fixed sleep.
  await page.getByText('กำลังโหลดข้อมูล Gantt...').waitFor({ state: 'detached', timeout: 20_000 }).catch(() => {
    // Already gone before we could even observe it (fast network) — not an error.
  })
  await page.getByTestId('gantt-body-canvas').waitFor({ state: 'visible', timeout: 20_000 })
}
