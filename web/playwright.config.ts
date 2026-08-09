import { defineConfig, devices } from '@playwright/test'

/**
 * S6-QA-01/02 (docs/10. Sprint 6, qa-engineer row): real-browser E2E for the Gantt chart —
 * `web/e2e/gantt-perf.spec.ts` (10,000-activity scroll frame-rate) and `web/e2e/visual/`
 * (prototype color/layering/data-date-line regression).
 *
 * There was no Playwright setup in this repo before this sprint — this config, the browser
 * install, and the npm scripts in `package.json` are new (S6-QA-01/02 scope, not pre-existing
 * infrastructure this task merely extends).
 *
 * Target under test: the real Vite React app talking to the real backend, via Vite's own dev
 * proxy (`vite.config.ts`'s `server.proxy['/api']`, `API_PROXY_TARGET` env, defaults to
 * `http://localhost:5000` — the real dockerized `docker-api-1` per `docs/perf/baseline.md`'s
 * runbook convention). This is a deliberate substitute for the `infra/docker` `web` container
 * (nginx): that container's `web.nginx.conf` has no `/api` reverse-proxy rule, so a browser
 * loading the built SPA from `docker-web-1` (port 8081) cannot reach the API at all (verified:
 * `POST /api/v1/auth/login` against `http://localhost:8081` returns a bare nginx `405`, not an
 * auth response — see this sprint's QA report for the full repro). That is a real infra gap,
 * flagged back to `devops-engineer`/`system-architect`, not something QA papers over here by
 * silently reusing a different mechanism without saying so.
 */
/**
 * A CM+-specific port, deliberately NOT Vite's default 5173: that default is shared by every Vite
 * project on a developer's machine, and colliding with one silently sent an entire E2E run against
 * an unrelated application (see the `webServer.reuseExistingServer` remarks below).
 */
const E2E_PORT = process.env.E2E_PORT ?? '5273'
const E2E_BASE_URL = `http://localhost:${E2E_PORT}`

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false, // the perf spec drives real wall-clock timing; parallel runs would contend for CPU and skew frame-rate numbers (docs/perf/baseline.md's own contended-CPU findings apply here too)
  forbidOnly: !!process.env.CI,
  retries: 0, // a perf/visual measurement that only "passes" after an automatic retry is not a real result — report the real number instead
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  timeout: 120_000,
  expect: {
    timeout: 15_000,
    toHaveScreenshot: { maxDiffPixelRatio: 0.02 },
  },
  use: {
    baseURL: E2E_BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: `npm run dev -- --port ${E2E_PORT} --strictPort`,
    url: E2E_BASE_URL,
    // Deliberately NOT `!process.env.CI`. `reuseExistingServer` cannot tell *whose* server is on
    // the port — it only checks that something answers. This bit us for real: port 5173 (Vite's
    // default, so shared by every Vite project on the machine) was already serving an unrelated
    // app, Playwright happily reused it, and the whole suite ran against the wrong application.
    // That run failed noisily only by luck; an unrelated app with similar labels could have
    // produced passing garbage instead. Always starting our own server on a CM+-specific port,
    // with --strictPort so a collision is a loud startup error rather than a silent fallback to
    // another port, removes the entire failure class. Override with E2E_PORT if 5273 ever clashes.
    reuseExistingServer: false,
    timeout: 60_000,
  },
})
