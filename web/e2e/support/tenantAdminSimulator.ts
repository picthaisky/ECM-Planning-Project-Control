import type { Page, Route } from '@playwright/test'

/**
 * Shared fixtures/helpers for S15-FE-01's E2E proof (`docs/10.` Sprint 15, frontend-developer row;
 * `web/e2e/tenant-admin-simulator.spec.ts`) — the tenant-admin policy version-history timeline and
 * routing simulator (US-15.2), proven through the real UI.
 *
 * **No real backend is used.** Docker cannot start on this workstation (needs Administrator — see
 * `docs/perf/gantt-frontend-s6.md` §3, `e2e/support/eacAdvanced.ts`'s identical constraint). Every
 * HTTP call this suite depends on is intercepted at the Playwright network layer (`page.route`) and
 * answered by a small, static mock of exactly the wire shapes
 * `TenantApprovalPoliciesController`/`ApprovalRoutingSimulationDto`/
 * `ApprovalPolicyVersionHistoryEntryDto` carry (transcribed from the real backend source — see
 * `features/tenant-admin/types.ts`'s own remarks) — real routing-arithmetic correctness (band
 * selection, escalation, ADR-0021 detection itself) is `backend`/`qa-engineer`'s own suite; this
 * proves the *frontend* renders the "would actually resolve" guarantee and the ADR-0021 warning
 * honestly, end-to-end through real UI interaction, not a re-derivation of the domain logic.
 */

export const FAKE_TENANT_ID = '9c858901-8a57-4791-81fe-4c455b099bc9'
export const FAKE_PROJECT_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'
/** A second, distinct project id used only to trigger the ADR-0021 corruption response below —
 * standing in for "the Admin trials a second scenario", not a real second seeded project. */
export const CORRUPT_SCENARIO_PROJECT_ID = 'a1a1a1a1-1111-4111-8111-a1a1a1a1a1a1'

export const ADMIN_USER = {
  email: 'admin@cmplus.test',
  password: 'not-checked-mock-backend',
  userId: '1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed',
  tenantId: FAKE_TENANT_ID,
  role: 'Admin',
}

const ACTIVE_VO_POLICY_ID = '33333333-3333-3333-3333-333333333333'
const CONFLICTING_VO_POLICY_ID = '44444444-4444-4444-4444-444444444444'

function voPolicy() {
  return {
    documentType: 'VariationOrder',
    version: 3,
    isActive: true,
    allowSelfApproval: false,
    cumulativeVoEscalationPct: '10.00',
    cumulativeVoEscalationRole: 'Executive',
    rules: [
      { stepNo: 1, minAmount: '0.00', maxAmount: '5000000.00', requiredRole: 'PM', quorumCount: 1 },
      { stepNo: 2, minAmount: '5000000.00', maxAmount: null, requiredRole: 'ProjectDirector', quorumCount: 1 },
    ],
  }
}

function voHistory() {
  return [
    {
      approvalPolicyId: '11111111-1111-1111-1111-111111111111',
      version: 1,
      isActive: false,
      effectiveFrom: '2026-01-05T03:00:00Z',
      effectiveTo: '2026-05-01T03:00:00Z',
      allowSelfApproval: true,
      cumulativeVoEscalationPct: null,
      cumulativeVoEscalationRole: null,
      ruleCount: 1,
      createdByUserId: ADMIN_USER.userId,
      createdAt: '2026-01-05T03:00:00Z',
      lastModifiedByUserId: null,
      lastModifiedAt: null,
    },
    {
      approvalPolicyId: '22222222-2222-2222-2222-222222222222',
      version: 2,
      isActive: false,
      effectiveFrom: '2026-05-01T03:00:00Z',
      effectiveTo: '2026-08-01T03:00:00Z',
      allowSelfApproval: false,
      cumulativeVoEscalationPct: null,
      cumulativeVoEscalationRole: null,
      ruleCount: 2,
      createdByUserId: ADMIN_USER.userId,
      createdAt: '2026-05-01T03:00:00Z',
      lastModifiedByUserId: ADMIN_USER.userId,
      lastModifiedAt: '2026-05-01T03:00:00Z',
    },
    {
      approvalPolicyId: ACTIVE_VO_POLICY_ID,
      version: 3,
      isActive: true,
      effectiveFrom: '2026-08-01T03:00:00Z',
      effectiveTo: null,
      allowSelfApproval: false,
      cumulativeVoEscalationPct: '10.00',
      cumulativeVoEscalationRole: 'Executive',
      ruleCount: 2,
      createdByUserId: ADMIN_USER.userId,
      createdAt: '2026-08-01T03:00:00Z',
      lastModifiedByUserId: ADMIN_USER.userId,
      lastModifiedAt: '2026-08-01T03:00:00Z',
    },
  ]
}

/** Installs the mocked `auth/login`, tenant approval-policy `GET`/`history`/`simulate` routes for
 * both document types. `simulate` branches purely on the request body's `projectId` — the
 * corruption scenario id triggers ADR-0021's ambiguous response, every other id gets a clean chain
 * — so a single mock backend can drive both DoD scenarios without per-test route swapping. */
export async function installTenantAdminMockBackend(page: Page): Promise<void> {
  await page.route('**/api/v1/auth/login', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'e2e-fake-jwt-admin',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: ADMIN_USER.userId,
        tenantId: ADMIN_USER.tenantId,
        role: ADMIN_USER.role,
      }),
    })
  })

  await page.route(
    `**/api/v1/tenants/${FAKE_TENANT_ID}/approval-policies?documentType=PaymentCertificate`,
    async (route: Route) => {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/problems/not-found',
          detail: 'ApprovalPolicyNotFound',
          status: 404,
        }),
      })
    },
  )
  await page.route(
    `**/api/v1/tenants/${FAKE_TENANT_ID}/approval-policies?documentType=VariationOrder`,
    async (route: Route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(voPolicy()) })
    },
  )

  await page.route(
    `**/api/v1/tenants/${FAKE_TENANT_ID}/approval-policies/PaymentCertificate/history`,
    async (route: Route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    },
  )
  await page.route(
    `**/api/v1/tenants/${FAKE_TENANT_ID}/approval-policies/VariationOrder/history`,
    async (route: Route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(voHistory()) })
    },
  )

  // A single `*` matches exactly one path segment (the `{documentType}` route parameter) — it does
  // not cross into `/simulate`, so this one route handles both document types.
  await page.route(`**/api/v1/tenants/${FAKE_TENANT_ID}/approval-policies/*/simulate`, async (route: Route) => {
    const url = route.request().url()
    const documentType = url.includes('/PaymentCertificate/') ? 'PaymentCertificate' : 'VariationOrder'
    const body = route.request().postDataJSON() as { projectId: string; amount: string | number }
    const amountString = String(body.amount)

    if (body.projectId === CORRUPT_SCENARIO_PROJECT_ID) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          documentType,
          projectId: body.projectId,
          inputAmount: amountString,
          routingAmount: amountString,
          approvalPolicyId: ACTIVE_VO_POLICY_ID,
          approvalPolicyVersion: 3,
          usedFallbackChain: false,
          steps: [{ stepNo: 1, requiredRole: 'PM', quorumCount: 1 }],
          escalationApplied: false,
          allowSelfApproval: false,
          multipleActivePoliciesDetected: true,
          ambiguousActivePolicies: [
            { approvalPolicyId: ACTIVE_VO_POLICY_ID, version: 3 },
            { approvalPolicyId: CONFLICTING_VO_POLICY_ID, version: 4 },
          ],
        }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        documentType,
        projectId: body.projectId,
        inputAmount: amountString,
        routingAmount: amountString,
        approvalPolicyId: ACTIVE_VO_POLICY_ID,
        approvalPolicyVersion: 3,
        usedFallbackChain: false,
        steps: [
          { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
          { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
        ],
        escalationApplied: documentType === 'VariationOrder' && Number(amountString) >= 5_000_000,
        allowSelfApproval: false,
        multipleActivePoliciesDetected: false,
        ambiguousActivePolicies: [],
      }),
    })
  })
}

/** Logs in as an Admin, then reaches `/tenant-admin` purely by real client-side navigation — never
 * `page.goto` post-login (the in-memory-only JWT in `authStore.ts` would be silently dropped by a
 * full navigation, `e2e/support/eacAdvanced.ts#loginAndOpenApp`'s identical remark). `/tenant-admin`
 * is a sibling of `/app/:projectId`, reachable only from the Topbar's Admin-only link
 * (`components/layout/Topbar.tsx`) once inside a project shell — there is no direct link from
 * `/select-project` — so this goes through the ordinary project-select step first. */
export async function loginAsAdminAndOpenTenantAdmin(page: Page): Promise<void> {
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

  await page.getByLabel('อีเมล').fill(ADMIN_USER.email)
  await page.getByLabel('รหัสผ่าน').fill(ADMIN_USER.password)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  await page.waitForURL((url) => url.pathname === '/select-project', { timeout: 15_000 })
  await page.getByLabel('รหัสโครงการ (Project ID)').fill(FAKE_PROJECT_ID)
  await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  await page.waitForURL(`**/app/${FAKE_PROJECT_ID}/**`, { timeout: 15_000 })

  await page.getByRole('link', { name: '⚙ Tenant Admin' }).click()
  await page.waitForURL('**/tenant-admin', { timeout: 15_000 })
}
