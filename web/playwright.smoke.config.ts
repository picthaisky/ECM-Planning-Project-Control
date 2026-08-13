import { defineConfig, devices } from '@playwright/test'

/**
 * S16-QA-02 (docs/10. Sprint 16A): config for the post-deploy smoke test (`e2e/smoke.spec.ts`).
 *
 * Unlike `playwright.config.ts`, this has NO `webServer` — it does not start a local dev server. It
 * runs against an already-DEPLOYED environment given by `SMOKE_BASE_URL` (e.g. the staging URL from
 * infra/staging). `ignoreHTTPSErrors` is on because staging terminates TLS with Caddy's internal CA
 * (self-signed) — see infra/staging/Caddyfile.
 *
 * Wire into cd.yml's post-deploy step: `SMOKE_BASE_URL=<deployed url> npm run test:e2e:smoke`.
 */
const SMOKE_BASE_URL = process.env.SMOKE_BASE_URL
if (!SMOKE_BASE_URL) {
  throw new Error(
    'SMOKE_BASE_URL is required for the post-deploy smoke test (e.g. https://staging.example). ' +
      'This config never starts a local server — it verifies a deployed environment.',
  )
}

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/smoke.spec.ts',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  // A smoke test may hit a transient post-deploy blip (a pod still warming); a couple of retries make
  // it a reliable gate rather than a flaky one — the opposite trade-off from the perf spec.
  retries: process.env.CI ? 2 : 1,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-smoke' }]],
  timeout: 90_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL: SMOKE_BASE_URL,
    ignoreHTTPSErrors: true, // Caddy `tls internal` on staging (self-signed)
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
