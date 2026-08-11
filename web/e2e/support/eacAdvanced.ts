import type { Page, Route } from '@playwright/test'

/**
 * Shared fixtures/helpers for S14-QA-01's E2E proof (`docs/10.` Sprint 14, qa-engineer row;
 * `web/e2e/eac-advanced.spec.ts`) — the 5-variant EAC selector (S14-FE-02) proven through the real
 * UI, fixtures A4/A5 (`.claude/knowledge/domain/evm-formulas.md`).
 *
 * **No real backend is used.** Docker cannot start on this workstation (needs Administrator — see
 * `docs/perf/gantt-frontend-s6.md` §3, `e2e/support/photoOffline.ts`'s identical constraint). Every
 * HTTP call is intercepted at the Playwright network layer (`page.route`) and answered by a small,
 * stateful in-memory mock of exactly the fields `GET /api/v1/projects/{id}`
 * (`ProjectDetail`/`ProjectEacConfig`, S14-FE-02's own fast-follow-flagged shape — see
 * `features/info/api.ts#getProject`'s remarks) and `GET .../evm` (`EvmResponseDto`) carry — real
 * `EacCalculator` correctness (all 5 variants, the edge-case matrix) is `backend`/`qa-engineer`'s
 * own test suite's job; this proves the *frontend* reads/writes those fields correctly end-to-end
 * through real UI interaction, not a re-derivation of the domain math.
 *
 * evm-formulas.md's Fixture A core inputs are held fixed throughout this mock:
 * BAC=1,000,000.00, PV=400,000.00, EV=300,000.00, AC=350,000.00 (CPI=0.857143, SPI=0.75), so the
 * three index-based variants' EAC never move for the same reason A1-A3 don't in the doc. Only
 * `BottomUpEtc`/`CustomPf` respond to the mock project's own `eacManualEtc`/
 * `eacCustomPerformanceFactor` — exactly the fields this suite exercises through the UI.
 */

export const FAKE_PROJECT_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

export const DEV_USER = {
  email: 'pm@cmplus.test',
  password: 'not-checked-mock-backend',
  userId: '1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed',
  tenantId: '9c858901-8a57-4791-81fe-4c455b099bc9',
  role: 'PM',
}

export interface MockProjectState {
  eacVariantDefault: string
  eacManualEtc: string | null
  eacCustomPerformanceFactor: string | null
  eacManualEtcStaleSince: string | null
}

export interface MockBackendOptions {
  initialProject?: Partial<MockProjectState>
}

function baseProjectFields() {
  return {
    id: FAKE_PROJECT_ID,
    name: 'Riverside Condominium Tower B',
    code: 'RCT-B',
    owner: 'Siam Riverside Development PLC',
    contractStart: '2025-10-01T00:00:00+07:00',
    contractFinish: '2027-03-31T00:00:00+07:00',
    bac: '1000000.00',
    contractValue: '1000000.00',
    retentionRate: '5.00',
    advanceRate: '10.00',
    retentionCapPercentage: null,
    retentionRelease1Percentage: '50.00',
    defectsLiabilityMonths: 24,
    advanceAmountPaid: null,
    advanceRecoveryMethod: 'ProRata',
    advanceRecoveryStartPct: null,
    advanceRecoveryRatePct: null,
    advanceRecoveryEndPct: null,
  }
}

/** Fixture A core (evm-formulas.md): BAC=1,000,000.00, PV=400,000.00, EV=300,000.00, AC=350,000.00. */
function buildEvmVariants(project: MockProjectState) {
  const bottomUpEtc =
    project.eacManualEtc === null
      ? { performanceFactor: null, etc: null, eac: null, vac: null, computable: false, reason: 'ManualEtcNotSet' }
      : {
          performanceFactor: null,
          etc: project.eacManualEtc,
          // AC (350,000.00) + ETC_manual — A4: 350,000 + 760,000 = 1,110,000.00.
          eac: (350000 + Number(project.eacManualEtc)).toFixed(2),
          vac: (1000000 - (350000 + Number(project.eacManualEtc))).toFixed(2),
          computable: true,
          reason: null,
        }

  const customPf =
    project.eacCustomPerformanceFactor === null
      ? { performanceFactor: null, etc: null, eac: null, vac: null, computable: false, reason: 'CustomPfNotSet' }
      : {
          performanceFactor: project.eacCustomPerformanceFactor,
          // ETC = PF_c * (BAC - EV) = PF_c * 700,000 — A5: 1.20 * 700,000 = 840,000.00.
          etc: (Number(project.eacCustomPerformanceFactor) * 700000).toFixed(2),
          eac: (350000 + Number(project.eacCustomPerformanceFactor) * 700000).toFixed(2),
          vac: (1000000 - (350000 + Number(project.eacCustomPerformanceFactor) * 700000)).toFixed(2),
          computable: true,
          reason: null,
        }

  return [
    { variant: 'CpiBased', performanceFactor: '1.166667', etc: '816666.67', eac: '1166666.67', vac: '-166666.67', computable: true, reason: null },
    { variant: 'Atypical', performanceFactor: '1.000000', etc: '700000.00', eac: '1050000.00', vac: '-50000.00', computable: true, reason: null },
    { variant: 'CpiSpiBased', performanceFactor: '1.555556', etc: '1088888.89', eac: '1438888.89', vac: '-438888.89', computable: true, reason: null },
    { variant: 'BottomUpEtc', ...bottomUpEtc },
    { variant: 'CustomPf', ...customPf },
  ]
}

const BEARER_TOKEN_PREFIX = 'e2e-fake-jwt-'

/**
 * Installs the mocked `auth/login`, `projects/{id}` (GET/PUT), `eac-advanced-inputs` (PUT),
 * `eac-default` (PUT), `evm` (GET), `evm/snapshots` (GET), and `import/jobs` (GET) routes.
 * `project` is mutable in-memory state, shared across every route this installs — this is what
 * makes the flow "configure inputs on Project Info -> see them reflected on EVM S-Curve" a genuine
 * round trip through this mock, not two independently-scripted responses.
 */
export async function installMockBackend(page: Page, options: MockBackendOptions = {}): Promise<MockProjectState> {
  const project: MockProjectState = {
    eacVariantDefault: 'CpiBased',
    eacManualEtc: null,
    eacCustomPerformanceFactor: null,
    eacManualEtcStaleSince: null,
    ...options.initialProject,
  }

  await page.route('**/api/v1/auth/login', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: `${BEARER_TOKEN_PREFIX}${DEV_USER.userId}`,
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: DEV_USER.userId,
        tenantId: DEV_USER.tenantId,
        role: DEV_USER.role,
      }),
    })
  })

  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}`, async (route: Route) => {
    const method = route.request().method()
    if (method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...baseProjectFields(), ...project }),
      })
      return
    }
    if (method === 'PUT') {
      // The general Project Info edit form (unused by this suite, but ProjectMasterCard renders on
      // the same screen) — echo the base fields back untouched plus whatever was submitted.
      const body = route.request().postDataJSON() as Record<string, unknown>
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...baseProjectFields(), ...body }),
      })
      return
    }
    await route.fallback()
  })

  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}/eac-advanced-inputs`, async (route: Route) => {
    const body = route.request().postDataJSON() as {
      eacManualEtc: string | null
      eacCustomPerformanceFactor: string | null
    }

    // Mirrors SetEacAdvancedInputsCommandHandler's own guard (S14-BE-03): cannot clear an input
    // while its variant is currently active.
    if (body.eacManualEtc === null && project.eacVariantDefault === 'BottomUpEtc') {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/problems/eac-manual-etc-required-for-bottom-up-etc',
          title: 'EacManualEtc must be configured before EacVariantDefault can be BottomUpEtc.',
          status: 400,
          detail: 'ProjectEacManualEtcRequiredForBottomUpEtc',
        }),
      })
      return
    }
    if (body.eacCustomPerformanceFactor === null && project.eacVariantDefault === 'CustomPf') {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/problems/eac-custom-performance-factor-required-for-custom-pf',
          title: 'EacCustomPerformanceFactor must be configured before EacVariantDefault can be CustomPf.',
          status: 400,
          detail: 'ProjectEacCustomPerformanceFactorRequiredForCustomPf',
        }),
      })
      return
    }

    project.eacManualEtc = body.eacManualEtc
    project.eacCustomPerformanceFactor = body.eacCustomPerformanceFactor
    // domain-rules.md §5.7: setting a fresh EacManualEtc always clears the staleness flag.
    if (body.eacManualEtc !== null) project.eacManualEtcStaleSince = null

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: FAKE_PROJECT_ID,
        eacManualEtc: project.eacManualEtc,
        eacCustomPerformanceFactor: project.eacCustomPerformanceFactor,
        eacManualEtcStaleSince: project.eacManualEtcStaleSince,
      }),
    })
  })

  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}/eac-default`, async (route: Route) => {
    const body = route.request().postDataJSON() as { variant: string }

    if (body.variant === 'BottomUpEtc' && project.eacManualEtc === null) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/problems/eac-manual-etc-required-for-bottom-up-etc',
          status: 400,
          detail: 'ProjectEacManualEtcRequiredForBottomUpEtc',
        }),
      })
      return
    }
    if (body.variant === 'CustomPf' && project.eacCustomPerformanceFactor === null) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/problems/eac-custom-performance-factor-required-for-custom-pf',
          status: 400,
          detail: 'ProjectEacCustomPerformanceFactorRequiredForCustomPf',
        }),
      })
      return
    }

    project.eacVariantDefault = body.variant
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ projectId: FAKE_PROJECT_ID, eacVariantDefault: project.eacVariantDefault }),
    })
  })

  // Registered as two separate routes, not one with an `.includes('/snapshots')` branch: Playwright
  // glob `*` does not cross `/` (only `**` does), so a single trailing `*` on `.../evm*` never
  // matches `.../evm/snapshots` (an extra path segment) in the first place — these are already
  // mutually exclusive by construction, not by branching inside one handler.
  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}/evm/snapshots*`, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}/evm*`, async (route: Route) => {
    const warnings = project.eacManualEtcStaleSince ? ['ManualEtcPredatesBacChange'] : []

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: FAKE_PROJECT_ID,
        dataDate: '2026-06-30T00:00:00+07:00',
        bac: '1000000.00',
        pv: '400000.00',
        ev: '300000.00',
        ac: '350000.00',
        sv: '-100000.00',
        cv: '-50000.00',
        spi: '0.750000',
        cpi: '0.857143',
        tcpiBac: '1.076923',
        tcpiEac: '0.857143',
        selectedVariant: project.eacVariantDefault,
        variants: buildEvmVariants(project),
        warnings,
      }),
    })
  })

  await page.route(`**/api/v1/projects/${FAKE_PROJECT_ID}/import/jobs*`, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  return project
}

/** Logs in and lands on `/app/:projectId/dashboard` via real UI interaction (never `page.goto`
 * post-login — the in-memory-only JWT in `authStore.ts` would be silently dropped by a full
 * navigation, mirrors `support/photoOffline.ts#loginAndOpenPhotoPage`'s identical remark). */
export async function loginAndOpenApp(page: Page, projectId: string = FAKE_PROJECT_ID): Promise<void> {
  await page.goto('/login')

  await page
    .getByRole('heading', { name: 'CM+ Project Control' })
    .waitFor({ state: 'visible', timeout: 15_000 })
    .catch(() => {
      throw new Error(
        `Expected the CM+ login page at ${page.url()}, but its identifying heading was never found. ` +
          'Something other than this app is almost certainly answering that port.',
      )
    })

  await page.getByLabel('อีเมล').fill(DEV_USER.email)
  await page.getByLabel('รหัสผ่าน').fill(DEV_USER.password)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  await page.waitForURL((url) => url.pathname === '/select-project' || url.pathname.startsWith('/app/'), {
    timeout: 15_000,
  })

  if (new URL(page.url()).pathname === '/select-project') {
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(projectId)
    await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  }

  await page.waitForURL(`**/app/${projectId}/**`, { timeout: 15_000 })
}

/** Navigates via the sidebar's own nav link, scoped to the `เมนูหลัก` nav landmark — some screens
 * (e.g. `EacAdvancedInputsCard`'s "ไปตั้งเป็นค่าเริ่มต้นที่หน้า EVM S-Curve" cross-link) render a
 * second link with the identical accessible name inside the page body, so an unscoped
 * `getByRole('link', { name: screenLabel })` is ambiguous. */
export async function openScreen(page: Page, projectId: string, screenLabel: string): Promise<void> {
  await page.getByLabel('เมนูหลัก').getByRole('link', { name: screenLabel, exact: true }).click()
}
