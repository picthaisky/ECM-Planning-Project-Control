import { defineConfig, devices } from '@playwright/test'

/**
 * S13-FE-02: a **separate** Playwright config, deliberately not folded into `playwright.config.ts`.
 *
 * `web/src/sw.ts` only exists as `/sw.js` after a real `vite build` (`vite.config.ts`'s
 * `serviceWorkerPlugin` runs on `apply: 'build'` only), and `registerServiceWorker.ts` itself only
 * registers anything when `import.meta.env.PROD` is true — neither is true under `vite dev`, which is
 * what the default `playwright.config.ts` deliberately runs against (see that file's own remarks).
 * Rather than switching the *whole* existing suite (gantt-perf, visual regression, the S12/S13 outbox
 * specs — all already passing against `vite dev`, none of them needing a real service worker) onto a
 * slower build+preview cycle and risking new flakiness in already-verified specs, this config runs
 * only `web/e2e/service-worker.spec.ts` against a real production build served by `vite preview`.
 *
 * Run via `npm run test:e2e:sw` (builds, then runs Playwright against the built output).
 */
const E2E_SW_PORT = process.env.E2E_SW_PORT ?? '5274'
const E2E_SW_BASE_URL = `http://localhost:${E2E_SW_PORT}`

export default defineConfig({
  testDir: './e2e',
  testMatch: ['service-worker.spec.ts'],
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-sw' }]],
  timeout: 120_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: E2E_SW_BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
    // A service worker registered against one origin cannot control a browser context started
    // without accepting that this is genuinely how it will run in production too — no special
    // Playwright-only bypass is used here; `serviceWorkers: 'allow'` is Playwright's own default,
    // named explicitly so this config's intent is not accidentally "fixed" later.
    serviceWorkers: 'allow',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    // A real production build (the exact artifact a deploy would ship), then Vite's own static
    // preview server — deliberately not `vite dev`, for the reasons in this file's own top comment.
    command: `npm run build && npx vite preview --port ${E2E_SW_PORT} --strictPort`,
    url: E2E_SW_BASE_URL,
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
