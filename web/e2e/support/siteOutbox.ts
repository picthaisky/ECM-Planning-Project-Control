import type { Page, Route } from '@playwright/test'
import { DEV_USER_A, FAKE_PROJECT_ID } from './photoOffline'
import type { FakeIdentity } from './photoOffline'

/**
 * Shared fixtures/helpers for the S13-FE-01 multi-kind offline suite
 * (`web/e2e/site-outbox-multi-kind.spec.ts`) — weather-log (Original + Correction) and progress-batch,
 * alongside photo. Mirrors `./photoOffline.ts`'s own conventions closely (no real backend; every HTTP
 * call intercepted via `page.route`) and reuses its `DEV_USER_A`/`FAKE_PROJECT_ID` so a test mixing
 * kinds shares one identity/project without re-deriving them.
 */

let requestCounter = 0

function headerCaseInsensitive(headers: Record<string, string>, name: string): string | null {
  const key = Object.keys(headers).find((h) => h.toLowerCase() === name.toLowerCase())
  return key ? headers[key] : null
}

const BEARER_TOKEN_PREFIX = 'e2e-fake-jwt-'

/** `POST /api/v1/auth/login` — standalone (not bundled with any one kind's write route, unlike
 * `photoOffline.ts#installMockPhotoBackend`) so a test can install exactly the kind-specific mocks it
 * needs alongside this one shared login handler. */
export async function installMockAuth(page: Page, identity: FakeIdentity = DEV_USER_A): Promise<void> {
  await page.route('**/api/v1/auth/login', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: `${BEARER_TOKEN_PREFIX}${identity.userId}`,
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: identity.userId,
        tenantId: identity.tenantId,
        role: identity.role,
      }),
    })
  })
}

/** `GET /api/v1/projects/{id}/wbs-tree` — an intentionally minimal, empty tree; the multi-kind suite
 * exercises the batch-progress *submit* path via the manual "+ เพิ่ม" (Activity ID) affordance, the
 * same one `WbsPage.test.tsx`'s "no list endpoint yet" tests already rely on, so the tree's own
 * content is irrelevant here. */
export async function installMockWbsTree(page: Page): Promise<void> {
  await page.route('**/api/v1/projects/*/wbs-tree', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ projectId: FAKE_PROJECT_ID, rootNodes: [] }),
    })
  })
}

/** `GET /api/v1/projects/{id}/weather-logs` — the server-confirmed register `WeatherPage` loads on
 * mount/after every sync; empty by default (a test overrides via `route.fulfill` again if it needs a
 * pre-existing row to correct). */
export async function installMockWeatherList(page: Page): Promise<void> {
  await page.route('**/api/v1/projects/*/weather-logs', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) })
  })
}

export interface WeatherAttempt {
  requestNumber: number
  kind: 'record' | 'correction'
  idempotencyKey: string | null
  bodyJson: unknown
}

export type WriteOutcome = { type: 'success' } | { type: 'http-error'; status: number; detail: string }

export interface FakeWeatherBackend {
  attempts: WeatherAttempt[]
}

/** `POST .../weather-logs` and `POST .../weather-logs/{id}/corrections`. `decide` lets a test force a
 * specific outcome (e.g. a 409 `IdempotencyPayloadMismatch`, mirroring S13-BE-01's real contract) per
 * attempt — mirrors `photoOffline.ts#installMockPhotoBackend`'s own `decide` callback shape. */
export async function installMockWeatherWrites(
  page: Page,
  options: { decide?: (info: WeatherAttempt) => WriteOutcome } = {},
): Promise<FakeWeatherBackend> {
  const decide = options.decide ?? (() => ({ type: 'success' as const }))
  const backend: FakeWeatherBackend = { attempts: [] }

  await page.route('**/api/v1/projects/*/weather-logs/*/corrections', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'POST') {
      await route.fallback()
      return
    }
    requestCounter += 1
    const idempotencyKey = headerCaseInsensitive(request.headers(), 'idempotency-key')
    let bodyJson: unknown
    try {
      bodyJson = request.postDataJSON()
    } catch {
      bodyJson = null
    }
    const attempt: WeatherAttempt = { requestNumber: requestCounter, kind: 'correction', idempotencyKey, bodyJson }
    backend.attempts.push(attempt)

    const outcome = decide(attempt)
    if (outcome.type === 'http-error') {
      await route.fulfill({
        status: outcome.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ type: `https://cmplus.dev/errors/${outcome.detail}`, title: outcome.detail, status: outcome.status, detail: outcome.detail }),
      })
      return
    }

    const body = bodyJson as Record<string, unknown>
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: `weather-correction-${requestCounter}`,
        projectId: FAKE_PROJECT_ID,
        logDate: body.logDate ?? new Date().toISOString(),
        condition: body.condition ?? 'Clear',
        conditionNote: body.conditionNote ?? null,
        rainfallMm: body.rainfallMm ?? null,
        impact: body.impact ?? 'NoImpact',
        impactNote: body.impactNote ?? null,
        hoursLost: body.hoursLost ?? null,
        workStoppage: body.impact !== undefined && body.impact !== 'NoImpact',
        entryKind: body.entryKind ?? 'Correction',
        correctsWeatherLogId: 'unused-in-mock',
        correctionReason: body.correctionReason ?? null,
        affectedActivityIds: body.affectedActivityIds ?? [],
        recordedByUserId: DEV_USER_A.userId,
        recordedAt: new Date().toISOString(),
      }),
    })
  })

  await page.route('**/api/v1/projects/*/weather-logs', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'POST') {
      await route.fallback()
      return
    }
    requestCounter += 1
    const idempotencyKey = headerCaseInsensitive(request.headers(), 'idempotency-key')
    let bodyJson: unknown
    try {
      bodyJson = request.postDataJSON()
    } catch {
      bodyJson = null
    }
    const attempt: WeatherAttempt = { requestNumber: requestCounter, kind: 'record', idempotencyKey, bodyJson }
    backend.attempts.push(attempt)

    const outcome = decide(attempt)
    if (outcome.type === 'http-error') {
      await route.fulfill({
        status: outcome.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ type: `https://cmplus.dev/errors/${outcome.detail}`, title: outcome.detail, status: outcome.status, detail: outcome.detail }),
      })
      return
    }

    const body = bodyJson as Record<string, unknown>
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: `weather-log-${requestCounter}`,
        projectId: FAKE_PROJECT_ID,
        logDate: body.logDate ?? new Date().toISOString(),
        condition: body.condition ?? 'Clear',
        conditionNote: body.conditionNote ?? null,
        rainfallMm: body.rainfallMm ?? null,
        impact: body.impact ?? 'NoImpact',
        impactNote: body.impactNote ?? null,
        hoursLost: body.hoursLost ?? null,
        workStoppage: body.impact !== undefined && body.impact !== 'NoImpact',
        entryKind: 'Original',
        correctsWeatherLogId: null,
        correctionReason: null,
        affectedActivityIds: body.affectedActivityIds ?? [],
        recordedByUserId: DEV_USER_A.userId,
        recordedAt: new Date().toISOString(),
      }),
    })
  })

  return backend
}

export interface ProgressAttempt {
  requestNumber: number
  idempotencyKey: string | null
  bodyJson: unknown
}

export interface FakeProgressBackend {
  attempts: ProgressAttempt[]
}

/** `POST .../progress/batch`. */
export async function installMockProgressWrites(
  page: Page,
  options: { decide?: (info: ProgressAttempt) => WriteOutcome } = {},
): Promise<FakeProgressBackend> {
  const decide = options.decide ?? (() => ({ type: 'success' as const }))
  const backend: FakeProgressBackend = { attempts: [] }

  await page.route('**/api/v1/projects/*/progress/batch', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'POST') {
      await route.fallback()
      return
    }
    requestCounter += 1
    const idempotencyKey = headerCaseInsensitive(request.headers(), 'idempotency-key')
    let bodyJson: unknown
    try {
      bodyJson = request.postDataJSON()
    } catch {
      bodyJson = null
    }
    const attempt: ProgressAttempt = { requestNumber: requestCounter, idempotencyKey, bodyJson }
    backend.attempts.push(attempt)

    const outcome = decide(attempt)
    if (outcome.type === 'http-error') {
      await route.fulfill({
        status: outcome.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ type: `https://cmplus.dev/errors/${outcome.detail}`, title: outcome.detail, status: outcome.status, detail: outcome.detail }),
      })
      return
    }

    const body = bodyJson as { entries?: unknown[] }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ entriesRecorded: body.entries?.length ?? 0 }),
    })
  })

  return backend
}

/** Logs in (real UI interaction, never a post-login `page.goto` — see `photoOffline.ts`'s identical
 * remarks on the in-memory-only JWT) and navigates to the named nav-link screen. */
export async function loginAndOpenScreen(
  page: Page,
  navLinkName: string,
  waitForSelectorLabel: string,
  projectId: string = FAKE_PROJECT_ID,
  identity: FakeIdentity = DEV_USER_A,
): Promise<void> {
  await page.goto('/login')
  await page
    .getByRole('heading', { name: 'CM+ Project Control' })
    .waitFor({ state: 'visible', timeout: 15_000 })
    .catch(() => {
      throw new Error(`Expected the CM+ login page at ${page.url()}, but its identifying heading was never found.`)
    })

  await page.getByLabel('อีเมล').fill(identity.email)
  await page.getByLabel('รหัสผ่าน').fill(identity.password)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  await page.waitForURL((url) => url.pathname === '/select-project' || url.pathname.startsWith('/app/'), {
    timeout: 15_000,
  })
  if (new URL(page.url()).pathname === '/select-project') {
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(projectId)
    await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  }
  await page.waitForURL(`**/app/${projectId}/**`, { timeout: 15_000 })

  const targetPath = `/app/${projectId}/${navLinkName === 'Weather Log' ? 'weather' : 'wbs'}`
  if (new URL(page.url()).pathname !== targetPath) {
    await page.getByRole('link', { name: navLinkName }).click()
    await page.waitForURL(`**${targetPath}`)
  }
  await page.getByText(waitForSelectorLabel).first().waitFor({ state: 'visible', timeout: 15_000 })
}

export { DEV_USER_A, DEV_USER_B, FAKE_PROJECT_ID, logout, readOutboxItemsFromIndexedDb } from './photoOffline'
