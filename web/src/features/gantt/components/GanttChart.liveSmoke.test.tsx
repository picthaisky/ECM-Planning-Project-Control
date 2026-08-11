import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { GanttChart } from './GanttChart'
import type { GanttActivityDto, GanttDto } from '../types'

/**
 * Opportunistic, real-backend verification — soft-skips when there is no live stack to talk to,
 * exactly like `backend/tests/CMPlus.Integration.Tests/Persistence/MigrationAndSeedSmokeTests.cs`'s
 * own established pattern for the same reason (this must never become a hard dependency for
 * `npm run test` in CI/every contributor's machine).
 *
 * When `VITE_CMPLUS_LIVE_API_BASE_URL` *is* set (this sprint's manual verification: the real
 * `docker compose` stack — `infra/docker/docker-compose.yml` — running S4-DB-02's seeded 10,000-
 * activity/5,000-node/15,000-relation dataset, project id
 * `2193e79c-a4c8-e7a5-0ffe-fb3f581af585`), this fetches the *real* `GET .../gantt` response over a
 * real HTTP call — no mock — and renders the real `GanttChart` against it, proving end-to-end that:
 * (a) the live payload's shape matches `GanttActivityDto` exactly (a decode/shape mismatch would
 * throw or produce garbage, not silently pass), and (b) the component stays DOM-bounded and does
 * not crash at the real 10,000-activity scale, not just against locally-synthesized fixtures (every
 * other test in this feature builds its own `buildActivities(n)`).
 *
 * `VITE_`-prefixed (read via `import.meta.env`, not `process.env`) to match this project's one
 * established env-var convention (`services/apiClient.ts`'s `VITE_API_BASE_URL`) — `tsconfig.app.json`
 * only types `vite/client`, not Node, so `process.env` isn't available/typed anywhere in `src`.
 *
 * What this does *not* replace: `web/e2e/gantt-perf.spec.ts` (S6-QA-01, real-browser 30s-scroll
 * frame-rate measurement) — jsdom has no real layout/paint/GPU, so this test cannot measure actual
 * frame rate, only "did the real payload render without error, within a bounded DOM".
 */

const LIVE_API_BASE_URL = import.meta.env.VITE_CMPLUS_LIVE_API_BASE_URL
const LIVE_EMAIL = import.meta.env.VITE_CMPLUS_LIVE_EMAIL ?? 'pm@siam-construction.dev'
const LIVE_PASSWORD = import.meta.env.VITE_CMPLUS_LIVE_PASSWORD ?? 'Dev@CMPlus2026!'
const LIVE_PROJECT_ID =
  import.meta.env.VITE_CMPLUS_LIVE_PROJECT_ID ?? '2193e79c-a4c8-e7a5-0ffe-fb3f581af585'

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth')
})

async function fetchRealGantt(): Promise<GanttDto> {
  const loginResponse = await fetch(`${LIVE_API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: LIVE_EMAIL, password: LIVE_PASSWORD }),
  })
  if (!loginResponse.ok) {
    throw new Error(`live login failed: HTTP ${loginResponse.status}`)
  }
  const { accessToken } = (await loginResponse.json()) as { accessToken: string }

  const ganttResponse = await fetch(`${LIVE_API_BASE_URL}/projects/${LIVE_PROJECT_ID}/gantt`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!ganttResponse.ok) {
    throw new Error(`live gantt fetch failed: HTTP ${ganttResponse.status}`)
  }
  return (await ganttResponse.json()) as GanttDto
}

function assertShapeIsRealGanttActivityDto(activity: GanttActivityDto) {
  expect(typeof activity.id).toBe('string')
  expect(typeof activity.wbsNodeId).toBe('string')
  expect(typeof activity.activityCode).toBe('string')
  expect(typeof activity.name).toBe('string')
  expect(typeof activity.plannedStart).toBe('string')
  expect(typeof activity.plannedFinish).toBe('string')
  expect(typeof activity.isCritical).toBe('boolean')
  expect(activity.actualStart === null || typeof activity.actualStart === 'string').toBe(true)
  expect(activity.actualFinish === null || typeof activity.actualFinish === 'string').toBe(true)
  expect(activity.totalFloat === null || typeof activity.totalFloat === 'number').toBe(true)
  expect(activity.freeFloat === null || typeof activity.freeFloat === 'number').toBe(true)
}

describe.runIf(!!LIVE_API_BASE_URL)('GanttChart against the real, live backend (manual verification, soft-skipped otherwise)', () => {
  it('renders the real S4-DB-02 10,000-activity dataset end-to-end without crashing, DOM-bounded', async () => {
    const gantt = await fetchRealGantt()

    expect(gantt.activities.length).toBeGreaterThanOrEqual(10_000)
    assertShapeIsRealGanttActivityDto(gantt.activities[0])

    const { container } = render(<GanttChart activities={gantt.activities} dataDateIso={null} />)

    expect(container.querySelectorAll('canvas').length).toBe(2)
    const renderedLabelRows = container.querySelectorAll('[data-gantt-row]')
    expect(renderedLabelRows.length).toBeGreaterThan(0)
    expect(renderedLabelRows.length).toBeLessThan(60)
  }, 30_000)
})
