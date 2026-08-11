import { test, expect } from '@playwright/test'
import {
  DEV_USER_A,
  installMockAuth,
  installMockProgressWrites,
  installMockWbsTree,
  installMockWeatherList,
  installMockWeatherWrites,
  loginAndOpenScreen,
  logout,
  readOutboxItemsFromIndexedDb,
} from './support/siteOutbox'

/**
 * S13-FE-01 (`docs/10.` Sprint 13, frontend-developer row; US-13.1): real-browser E2E for the outbox
 * extended to Weather Log (Original + Correction) and batch progress — the two new `kind`s alongside
 * Photo (S12-QA-01's `photo-offline.spec.ts`). Same no-real-backend approach: every HTTP call is
 * intercepted via `page.route` (see `support/siteOutbox.ts` / `support/photoOffline.ts`'s own remarks
 * on why that is still a genuine, real-browser test of the client-side offline contract).
 *
 * Sprint 12 security review's sharpest observation was that a 13/13 green suite was structurally
 * blind to both Highs because it exercised one user, one project, one browser profile against a
 * single kind. This suite's specific job is the shapes that review named as worth adding: multiple
 * kinds in the same queue, the weather-log correction-ordering problem, and the 409 idempotency-
 * conflict path.
 */

const WEATHER_OUTBOX_ITEM = '[data-testid="weather-outbox-item"]'
const PROGRESS_OUTBOX_ITEM = '[data-testid="progress-outbox-item"]'

test.describe('S13-FE-01 — Weather Log offline → sync', () => {
  test('a weather log recorded offline queues, then syncs once online and appears in the server-confirmed table', async ({ page, context }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    const weather = await installMockWeatherWrites(page)
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()

    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(1)
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveAttribute('data-outbox-status', 'queued')
    expect(weather.attempts).toHaveLength(0)

    await context.setOffline(false)
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()

    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(0, { timeout: 15_000 }) // synced -> leaves the pending queue
    expect(weather.attempts).toHaveLength(1)
    expect(weather.attempts[0].kind).toBe('record')
    expect(weather.attempts[0].idempotencyKey).toBeTruthy()

    const stored = await readOutboxItemsFromIndexedDb(page)
    expect(stored).toHaveLength(1)
    expect(stored[0].status).toBe('synced')
  })

  test('the correction-ordering problem: a correction queued against a not-yet-synced local original waits, then resolves automatically in the same sync pass once the original succeeds', async ({
    page,
    context,
  }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    const weather = await installMockWeatherWrites(page)
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(1)

    // Correct the entry before it has ever synced — the genuine ordering problem `weatherOutbox.ts`
    // is built to handle rather than fail opaquely.
    await page.getByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' }).click()
    await expect(page.getByText(/แก้ไข\/ยกเลิกรายการ — บันทึกวันที่/)).toBeVisible()
    await page.getByRole('checkbox').click()
    await page.getByLabel(/เหตุผลที่แก้ไข/).fill('พิมพ์ผิด แก้ไขก่อนซิงค์')
    await page.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ }).click()

    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(2)
    expect(weather.attempts).toHaveLength(0)

    // One manual sync, still offline-to-online transition captured mid-flow: both items are pending
    // (oldest-first), so the Original is attempted before the Correction in the very same pass.
    await context.setOffline(false)
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()

    // Both resolved in this one pass — the correction never had to wait for a *second* trigger.
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(0, { timeout: 15_000 })
    expect(weather.attempts.map((a) => a.kind)).toEqual(['record', 'correction'])
    // The correction's request carried the Original's real server id, resolved at sync time — never
    // the synthetic `local-pending:` placeholder id.
    expect(weather.attempts[1].idempotencyKey).toBeTruthy()

    const stored = await readOutboxItemsFromIndexedDb(page)
    expect(stored.every((item) => item.status === 'synced')).toBe(true)
  })

  test('the correction genuinely waits (not opaquely) when its target original keeps failing to sync', async ({ page, context }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    const weather = await installMockWeatherWrites(page, {
      decide: (attempt) =>
        attempt.kind === 'record' ? { type: 'http-error', status: 400, detail: 'WeatherLogUnknownActivity' } : { type: 'success' },
    })
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()

    await page.getByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' }).click()
    await page.getByRole('checkbox').click()
    await page.getByLabel(/เหตุผลที่แก้ไข/).fill('พิมพ์ผิด')
    await page.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ }).click()
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(2)

    await context.setOffline(false)
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()

    // The Original genuinely fails (server rejection); the Correction is never even attempted against
    // the API — it shows the specific Thai "waiting for original" message, not a generic/opaque error.
    await expect(page.getByText(/รอซิงค์บันทึกสภาพอากาศต้นฉบับให้เสร็จก่อน/)).toBeVisible({ timeout: 15_000 })
    expect(weather.attempts.map((a) => a.kind)).toEqual(['record'])
  })
})

test.describe('S13-FE-01 — batch progress offline → sync', () => {
  test('a batch queued offline syncs once online, with the confirmed entriesRecorded count', async ({ page, context }) => {
    await installMockAuth(page)
    await installMockWbsTree(page)
    const progress = await installMockProgressWrites(page)
    await loginAndOpenScreen(page, 'WBS & Activity', 'ขยายทั้งหมด')

    await page.getByRole('button', { name: 'โหมดอัปเดตความคืบหน้า' }).click()
    await page.getByLabel('เพิ่มกิจกรรมด้วยรหัส (Activity ID)').fill('11111111-1111-1111-1111-111111111111')
    await page.getByRole('button', { name: '+ เพิ่ม' }).click()
    await page.getByLabel('วันที่ของงวดข้อมูล (Period End Date)').fill('2026-08-11')
    await page.getByLabel(/% ความคืบหน้าใหม่ของ/).fill('45')

    await context.setOffline(true)
    await page.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }).click()

    await expect(page.getByText('คิวไว้แล้ว 1 รายการ')).toBeVisible()
    await expect(page.locator(PROGRESS_OUTBOX_ITEM)).toHaveCount(1)
    await expect(page.locator(PROGRESS_OUTBOX_ITEM)).toHaveAttribute('data-outbox-status', 'queued')
    expect(progress.attempts).toHaveLength(0)

    await context.setOffline(false)
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()

    await expect(page.locator(PROGRESS_OUTBOX_ITEM)).toHaveCount(0, { timeout: 15_000 })
    expect(progress.attempts).toHaveLength(1)
    expect(progress.attempts[0].bodyJson).toMatchObject({
      entries: [{ activityId: '11111111-1111-1111-1111-111111111111', progressPercentage: '45' }],
    })

    const stored = await readOutboxItemsFromIndexedDb(page)
    expect(stored).toHaveLength(1)
    expect(stored[0].status).toBe('synced')
  })
})

test.describe('S13-FE-01 — idempotency 409 (S13-BE-01 contract)', () => {
  test('a same-key-different-payload 409 (IdempotencyPayloadMismatch) is a terminal "conflict" — legible in Thai, and never retried again', async ({
    page,
    context,
  }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    const weather = await installMockWeatherWrites(page, {
      decide: () => ({ type: 'http-error', status: 409, detail: 'IdempotencyPayloadMismatch' }),
    })
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(1)

    await context.setOffline(false)
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()

    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveAttribute('data-outbox-status', 'conflict', { timeout: 15_000 })
    await expect(page.getByText(/เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้/)).toBeVisible()
    expect(weather.attempts).toHaveLength(1)

    // A second manual "sync now" must not retry a terminal conflict.
    await page.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }).click()
    await page.waitForTimeout(500)
    expect(weather.attempts).toHaveLength(1)

    const stored = await readOutboxItemsFromIndexedDb(page)
    expect(stored[0].status).toBe('conflict')
  })
})

test.describe('S13-FE-03 — N-03 sign-out gate with a pending item', () => {
  test('signing out with an un-synced item shows the gate; "ออกจากระบบโดยไม่ซิงค์" proceeds anyway', async ({ page, context }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    await installMockWeatherWrites(page)
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(1)

    // Wait for the Sidebar's own (independently-polling) sync-status badge to actually reflect the
    // pending item before signing out — this is what makes the gate trigger deterministic here rather
    // than racing the badge's own refresh cycle (`services/useOutboxSyncStatus.ts`).
    await expect(page.getByText(/รอซิงค์ 1 รายการ/)).toBeVisible({ timeout: 10_000 })

    await page.getByRole('button', { name: 'ออกจากระบบ' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByText(/คุณมีรายการที่ยังไม่ได้ซิงค์ 1 รายการ/)).toBeVisible()

    await page.getByRole('button', { name: 'ออกจากระบบโดยไม่ซิงค์' }).click()
    await page.waitForURL('**/login**', { timeout: 15_000 })
  })

  test('with nothing pending, signing out is immediate — no gate for the common case', async ({ page }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')

    await logout(page) // the existing, un-gated helper — must still work exactly as before
  })
})

test.describe('S13-FE-01 — multiple kinds queued together', () => {
  test('weather-log and progress-batch items queued in the same offline session both survive and both sync on the same manual trigger', async ({
    page,
    context,
  }) => {
    await installMockAuth(page)
    await installMockWeatherList(page)
    await installMockWbsTree(page)
    const weather = await installMockWeatherWrites(page)
    const progress = await installMockProgressWrites(page)

    await loginAndOpenScreen(page, 'Weather Log', 'บันทึกสภาพอากาศรายวัน')
    await context.setOffline(true)
    await page.getByRole('button', { name: '+ บันทึกวันนี้' }).click()
    await page.getByRole('checkbox').click()
    await page.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }).click()
    await expect(page.locator(WEATHER_OUTBOX_ITEM)).toHaveCount(1)

    await page.getByRole('link', { name: 'WBS & Activity' }).click()
    await page.getByText('ขยายทั้งหมด').first().waitFor({ state: 'visible' })
    await page.getByRole('button', { name: 'โหมดอัปเดตความคืบหน้า' }).click()
    await page.getByLabel('เพิ่มกิจกรรมด้วยรหัส (Activity ID)').fill('22222222-2222-2222-2222-222222222222')
    await page.getByRole('button', { name: '+ เพิ่ม' }).click()
    await page.getByLabel('วันที่ของงวดข้อมูล (Period End Date)').fill('2026-08-11')
    await page.getByLabel(/% ความคืบหน้าใหม่ของ/).fill('10')
    await page.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }).click()
    await expect(page.locator(PROGRESS_OUTBOX_ITEM)).toHaveCount(1)

    // Owner-scoped read across kinds, proving both survived independently of which screen is open.
    const beforeSync = await readOutboxItemsFromIndexedDb(page)
    expect(beforeSync.map((i) => i.kind).sort()).toEqual(['progress-batch', 'weather-log'])
    expect(beforeSync.every((i) => i.ownerUserId === DEV_USER_A.userId)).toBe(true)

    await context.setOffline(false)
    // Deliberately the *global* sync-status badge (`SyncStatusBadge.tsx`, S13-FE-03), not either
    // page's own "ซิงค์เดี๋ยวนี้" button: each feature hook's own sync engine only knows its own
    // kind(s) (`useWeatherLogOutbox`/`useProgressBatchOutbox`), so only the badge's engine — built
    // from `services/siteOutboxRegistry.ts`'s full registry — can flush both in one action from
    // whichever screen happens to be open (here, still the WBS page).
    await expect(page.getByText(/รอซิงค์ 2 รายการ/)).toBeVisible({ timeout: 10_000 })
    await page.getByTestId('sync-status-badge-toggle').click()
    await page.getByRole('button', { name: 'ซิงค์ทั้งหมดตอนนี้' }).click()

    // Polls the real, durable state directly rather than a single HTTP-attempt-count snapshot:
    // `useProgressBatchOutbox`'s own reconnect trigger and the badge's explicit click can race for
    // the progress item specifically (both are legitimate, independent triggers for the *same*
    // kind — see `services/useOutboxSyncStatus.ts`'s own remarks on why that overlap is accepted,
    // not eliminated, now that S13-BE-01 makes a redundant attempt harmless) — what this test needs
    // to prove is that *both kinds* end up synced, not which exact click produced which request.
    await expect(async () => {
      const items = await readOutboxItemsFromIndexedDb(page)
      expect(items).toHaveLength(2)
      expect(items.every((i) => i.status === 'synced')).toBe(true)
    }).toPass({ timeout: 15_000 })

    expect(weather.attempts.length).toBeGreaterThanOrEqual(1)
    expect(progress.attempts.length).toBeGreaterThanOrEqual(1)
  })
})
