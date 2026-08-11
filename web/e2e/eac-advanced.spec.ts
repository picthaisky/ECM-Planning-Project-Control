import { test, expect } from '@playwright/test'
import { FAKE_PROJECT_ID, installMockBackend, loginAndOpenApp, openScreen } from './support/eacAdvanced'

/**
 * S14-QA-01 (`docs/10.` Sprint 14, qa-engineer row; US-14.3*): real-browser E2E for the S14-FE-02
 * 5-variant EAC selector — DoD: "A4/A5 ผ่านผ่าน UI จริง" (A4/A5 pass through the real UI), and the
 * disabled-with-explanation / cannot-clear-while-active guards, both surfaced in Thai.
 *
 * **No backend runs for this suite** — see `./support/eacAdvanced.ts`'s top-of-file comment for the
 * full rationale (Docker cannot start on this workstation) and for exactly what this does and does
 * not prove: every HTTP call is intercepted at the Playwright network layer and answered by a
 * small, *stateful* in-memory mock of the real DTO shapes, so a value entered on Project Info
 * genuinely round-trips through to what the EVM screen reads next — this proves the frontend's own
 * wiring/state management, not the backend `EacCalculator`'s arithmetic (that is backend/qa-
 * engineer's own suite).
 */

test.describe('S14-QA-01 — 5-variant EAC selector (US-14.3*)', () => {
  test('DoD: BottomUpEtc/CustomPf are disabled with a Thai explanation when their input is unset, and the explanation links to Project Info', async ({
    page,
  }) => {
    await installMockBackend(page)
    await loginAndOpenApp(page)
    await openScreen(page, FAKE_PROJECT_ID, 'EVM S-Curve')

    await expect(page.getByTestId('evm-metrics-grid')).toBeVisible()

    const bottomUpEtcRadio = page.getByRole('radio', { name: /Bottom-Up ETC/ })
    const customPfRadio = page.getByRole('radio', { name: /Custom PF/ })
    await expect(bottomUpEtcRadio).toBeDisabled()
    await expect(customPfRadio).toBeDisabled()

    await expect(page.getByText(/ยังไม่ได้กรอกประมาณการงานที่เหลือ \(Bottom-Up ETC\)/)).toBeVisible()
    await expect(page.getByText(/ยังไม่ได้กำหนดตัวคูณผลการดำเนินงานเอง/)).toBeVisible()

    const links = page.getByRole('link', { name: 'ไปกรอกที่หน้าข้อมูลโครงการ →' })
    await expect(links).toHaveCount(2)
    await links.first().click()
    await page.waitForURL(`**/app/${FAKE_PROJECT_ID}/info`)
    await expect(page.getByText('ตั้งค่า EAC ขั้นสูง')).toBeVisible()
  })

  test('DoD: fixtures A4/A5 exactly, through the real UI, after configuring both inputs on Project Info', async ({
    page,
  }) => {
    await installMockBackend(page)
    await loginAndOpenApp(page)
    await openScreen(page, FAKE_PROJECT_ID, 'ข้อมูลโครงการ')

    await expect(page.getByText('ตั้งค่า EAC ขั้นสูง')).toBeVisible()
    await page.getByLabel(/Bottom-Up ETC/).fill('760000')
    await page.getByLabel('Custom Performance Factor').fill('1.20')
    await page.getByRole('button', { name: 'บันทึก' }).click()

    // The save round-trips through the mock's own in-memory project state — no local-only optimism.
    await expect(page.getByLabel(/Bottom-Up ETC/)).toHaveValue('760000')

    await openScreen(page, FAKE_PROJECT_ID, 'EVM S-Curve')
    await expect(page.getByTestId('evm-metrics-grid')).toBeVisible()

    const bottomUpEtcRadio = page.getByRole('radio', { name: /Bottom-Up ETC/ })
    const customPfRadio = page.getByRole('radio', { name: /Custom PF/ })
    await expect(bottomUpEtcRadio).toBeEnabled()
    await expect(customPfRadio).toBeEnabled()

    // A4 (evm-formulas.md): BottomUpEtc, ETC_manual=760,000.00 -> EAC 1,110,000.00.
    await bottomUpEtcRadio.click()
    await expect(page.getByTestId('evm-metrics-grid')).toContainText('1,110,000.00 บาท')
    await expect(page.getByTestId('evm-metrics-grid')).toContainText('760,000.00 บาท')

    // A5 (evm-formulas.md): CustomPf, PF_c=1.20 -> EAC 1,190,000.00.
    await customPfRadio.click()
    await expect(page.getByTestId('evm-metrics-grid')).toContainText('1,190,000.00 บาท')
    await expect(page.getByTestId('evm-metrics-grid')).toContainText('840,000.00 บาท')
  })

  test('DoD: selecting BottomUpEtc as the project default, then trying to clear its input, is rejected with a Thai message pointing back at EVM', async ({
    page,
  }) => {
    await installMockBackend(page, { initialProject: { eacManualEtc: '760000.00' } })
    await loginAndOpenApp(page)
    await openScreen(page, FAKE_PROJECT_ID, 'EVM S-Curve')

    await expect(page.getByTestId('evm-metrics-grid')).toBeVisible()
    await page.getByRole('radio', { name: /Bottom-Up ETC/ }).click()
    await page.getByRole('button', { name: 'ตั้งเป็นค่าเริ่มต้นของโครงการ' }).click()
    await expect(page.getByText('ค่าเริ่มต้นของโครงการ: Bottom-Up ETC')).toBeVisible()

    await openScreen(page, FAKE_PROJECT_ID, 'ข้อมูลโครงการ')
    await expect(page.getByLabel(/Bottom-Up ETC/)).toHaveValue('760000.00')

    await page.getByLabel(/Bottom-Up ETC/).fill('')
    await page.getByRole('button', { name: 'บันทึก' }).click()

    await expect(page.getByText(/ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน/)).toBeVisible()
    // Never silently accepted — the field still shows the real, unchanged server value (this
    // click's own request was rejected before it was ever applied server-side).
    await openScreen(page, FAKE_PROJECT_ID, 'EVM S-Curve')
    await expect(page.getByText('ค่าเริ่มต้นของโครงการ: Bottom-Up ETC')).toBeVisible()
  })

  test('domain-rules.md §5.7: the ManualEtcPredatesBacChange warning on EVM links to Project Info, and re-entering the value there clears it', async ({
    page,
  }) => {
    const project = await installMockBackend(page, {
      initialProject: { eacManualEtc: '760000.00', eacManualEtcStaleSince: '2026-08-01T00:00:00+07:00' },
    })
    await loginAndOpenApp(page)
    await openScreen(page, FAKE_PROJECT_ID, 'EVM S-Curve')

    await expect(page.getByText(/มี Variation Order ที่อนุมัติแล้วเปลี่ยนงบประมาณ/)).toBeVisible()
    const fixLink = page.getByRole('link', { name: 'ไปกรอกค่าประมาณการใหม่ที่หน้าข้อมูลโครงการ →' })
    await fixLink.click()
    await page.waitForURL(`**/app/${FAKE_PROJECT_ID}/info`)

    await expect(page.getByText(/มี Variation Order ที่อนุมัติแล้วเปลี่ยน BAC หลังจากกรอกค่า Bottom-Up ETC/)).toBeVisible()

    await page.getByLabel(/Bottom-Up ETC/).fill('900000')
    await page.getByRole('button', { name: 'บันทึก' }).click()

    await expect(page.getByText(/มี Variation Order ที่อนุมัติแล้วเปลี่ยน BAC หลังจากกรอกค่า Bottom-Up ETC/)).toHaveCount(0)
    expect(project.eacManualEtcStaleSince).toBeNull()
  })
})
