import { test, expect } from '@playwright/test'
import {
  CORRUPT_SCENARIO_PROJECT_ID,
  FAKE_PROJECT_ID,
  installTenantAdminMockBackend,
  loginAsAdminAndOpenTenantAdmin,
} from './support/tenantAdminSimulator'

/**
 * S15-FE-01 (`docs/10.` Sprint 15, frontend-developer row; US-15.2): real-browser E2E for the
 * Tenant Admin approval-policy version-history timeline and routing simulator. DoD: "Admin เห็น
 * ลำดับเวลาการเปลี่ยน policy และทดลอง routing ได้" — the Admin sees a chronological timeline of policy
 * changes and can trial routing without creating a document.
 *
 * **No backend runs for this suite** — see `./support/tenantAdminSimulator.ts`'s top-of-file
 * comment for the full rationale and for exactly what this does and does not prove.
 */

test.describe('S15-FE-01 — approval-policy version history + routing simulator (US-15.2)', () => {
  test('DoD: the Admin sees the version-history timeline and can trial routing without creating any document', async ({
    page,
  }) => {
    await installTenantAdminMockBackend(page)
    await loginAsAdminAndOpenTenantAdmin(page)

    await page.getByRole('tab', { name: 'Variation Order' }).click()

    // The timeline: every version, newest first, exactly one flagged Active.
    const history = page.getByTestId('approval-policy-history')
    await expect(history.getByText('ประวัติเวอร์ชันนโยบาย (Version History)')).toBeVisible()
    const historyItems = history.getByRole('listitem')
    await expect(historyItems).toHaveCount(3)
    await expect(historyItems.nth(0)).toContainText('v3')
    await expect(historyItems.nth(0)).toContainText('ใช้งานอยู่ (Active)')
    await expect(historyItems.nth(2)).toContainText('v1')

    // The simulator: a hypothetical amount against the real (fake) project, resolved via the real
    // POST /simulate endpoint — no document is created by this action.
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(FAKE_PROJECT_ID)
    await page.getByLabel('จำนวนเงินสมมติ (บาท)').fill('6000000')
    await page.getByRole('button', { name: 'ทดลอง routing' }).click()

    const result = page.getByTestId('routing-simulation-result')
    await expect(result).toBeVisible()
    await expect(result).toContainText('จะถูก resolve ตามนโยบาย')
    await expect(result).toContainText('v3')
    await expect(result.getByRole('table')).toContainText('Project Director')
    // Above the VO cumulative-escalation threshold at this amount — the honest reason surfaces too.
    await expect(result).toContainText('มีขั้นตอน escalation เพิ่มเติม')
    // No ADR-0021 warning for a clean, unambiguous resolution.
    await expect(result.getByText(/ADR-0021/)).toHaveCount(0)
  })

  test('ADR-0021: a corrupted tenant policy state (two simultaneously-active versions) surfaces as a clear warning, never a normal-looking chain', async ({
    page,
  }) => {
    await installTenantAdminMockBackend(page)
    await loginAsAdminAndOpenTenantAdmin(page)
    await page.getByRole('tab', { name: 'Variation Order' }).click()

    await page.getByLabel('รหัสโครงการ (Project ID)').fill(CORRUPT_SCENARIO_PROJECT_ID)
    await page.getByLabel('จำนวนเงินสมมติ (บาท)').fill('1000000')
    await page.getByRole('button', { name: 'ทดลอง routing' }).click()

    const result = page.getByTestId('routing-simulation-result')
    const warning = result.getByRole('alert')
    await expect(warning).toContainText('ADR-0021')
    await expect(warning).toContainText('พบนโยบายที่ Active พร้อมกันมากกว่า 1 เวอร์ชัน')
    await expect(warning).toContainText('v3')
    await expect(warning).toContainText('v4')
    // The chain itself is still shown underneath — the point is honesty, not hiding the result.
    await expect(result.getByRole('table')).toBeVisible()
  })

  test('Payment Certificate: the simulator states up front that VO cumulative-escalation does not apply to this document type', async ({
    page,
  }) => {
    await installTenantAdminMockBackend(page)
    await loginAsAdminAndOpenTenantAdmin(page)
    // Default tab is PaymentCertificate.

    const simulator = page.getByTestId('approval-routing-simulator')
    await expect(simulator).toContainText('ไม่มีเงื่อนไข escalation สะสมของ VO')

    await page.getByLabel('รหัสโครงการ (Project ID)').fill(FAKE_PROJECT_ID)
    await page.getByLabel('จำนวนเงินสมมติ (บาท)').fill('2000000')
    await page.getByRole('button', { name: 'ทดลอง routing' }).click()

    const result = page.getByTestId('routing-simulation-result')
    await expect(result).toContainText('เงื่อนไข escalation สะสมของ VO ไม่เกี่ยวข้องกับเอกสารประเภทนี้')
  })
})
