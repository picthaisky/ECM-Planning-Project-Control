import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { WeatherPage } from './WeatherPage'
import * as weatherApi from './api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { EotEvaluationDto, WeatherLogDto } from './types'

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 440 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    listWeatherLogs: vi.fn(),
    recordWeatherLog: vi.fn(),
    recordWeatherLogCorrection: vi.fn(),
    evaluateEot: vi.fn(),
  }
})

/** S13-FE-01: both writes now enqueue into the real (fake-indexeddb-polyfilled) `cmplus-outbox`
 * database before syncing — mirrors `features/photo/usePhotoOutbox.test.ts`'s identical reset
 * helper, needed for the same reason (isolate each test's outbox state under the fixed production
 * database name). */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

const sampleLog: WeatherLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  logDate: '2026-07-08T00:00:00+07:00',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '61.00',
  impact: 'FullStoppage',
  impactNote: 'หยุดงานภายนอกทั้งวัน',
  hoursLost: '8.00',
  workStoppage: true,
  entryKind: 'Original',
  correctsWeatherLogId: null,
  correctionReason: null,
  affectedActivityIds: ['activity-1'],
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-08T09:00:00+07:00',
}

const sampleEvaluation: EotEvaluationDto = {
  id: 'eval-1',
  projectId: 'project-1',
  windowStart: '2026-07-01T00:00:00+07:00',
  windowEnd: '2026-07-31T00:00:00+07:00',
  evaluatedAt: '2026-08-01T00:00:00+07:00',
  evaluatedByUserId: 'user-1',
  criticalityBasis: 'Contemporaneous',
  confidence: 'Substantiated',
  asScheduledDurationDays: 15,
  impactedDurationDays: 16,
  eotEligibleDays: 1,
  countableStoppageDayCount: 1,
  distinctCountableDateCount: 1,
  unattributedStoppageDayCount: 0,
  concurrencyAssessed: false,
  entitlementBasisAssessed: false,
  latestNoticeDate: null,
  noticeWindowExpired: null,
  runs: [],
  sources: [],
  drivers: [],
}

function renderPage(role: UserRole = 'PM') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/weather']}>
      <Routes>
        <Route path="/app/:projectId/weather" element={<WeatherPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('WeatherPage', () => {
  let onLineSpy: ReturnType<typeof vi.spyOn>

  beforeEach(async () => {
    await resetOutboxDatabase()
    useAuthStore.getState().logout()
    vi.mocked(weatherApi.listWeatherLogs).mockReset()
    vi.mocked(weatherApi.recordWeatherLog).mockReset()
    vi.mocked(weatherApi.recordWeatherLogCorrection).mockReset()
    vi.mocked(weatherApi.evaluateEot).mockReset()
    // Offline by default (mirrors `features/photo/usePhotoOutbox.test.ts`'s identical setup) so the
    // outbox's mount-time auto-sync trigger (`syncTriggers.ts`) never fires an *extra*,
    // test-surprising `listWeatherLogs`/write call underneath a test's own explicit assertions —
    // tests that need a write to actually reach the (mocked) API click "ซิงค์เดี๋ยวนี้" explicitly.
    onLineSpy = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
  })

  afterEach(async () => {
    onLineSpy.mockRestore()
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('loads the register and shows the tiles + table populated from it', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([sampleLog])
    renderPage()

    // Both the rain-day and stoppage-day tiles read "1 วัน" for this single fixture entry.
    await waitFor(() => expect(screen.getAllByText('1 วัน')).toHaveLength(2))
    expect(screen.getByText('หยุดงานภายนอกทั้งวัน')).toBeInTheDocument() // table row
  })

  it('a write-role user can open the record modal, and the entry queues then syncs on demand (ADR-0005: always outbox, never a direct call)', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValue([])
    vi.mocked(weatherApi.recordWeatherLog).mockResolvedValueOnce(sampleLog)
    renderPage('Site')

    await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึกวันนี้' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: '+ บันทึกวันนี้' }))

    expect(screen.getByText('บันทึกสภาพอากาศไม่สามารถแก้ไขหรือลบได้หลังบันทึก')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    // Enqueued, but this test runs "offline" (`beforeEach`'s default) — the entry sits queued until
    // a real trigger fires; the DoD's own manual-retry affordance is that trigger here.
    await waitFor(() => expect(screen.getByTestId('weather-outbox-item')).toBeInTheDocument())
    expect(weatherApi.recordWeatherLog).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }))

    // Both real, live `recordWeatherLog` and the reload-after-sync `listWeatherLogs` fire.
    await waitFor(() =>
      expect(weatherApi.recordWeatherLog).toHaveBeenCalledWith('project-1', expect.any(Object), expect.any(String)),
    )
    await waitFor(() => expect(weatherApi.listWeatherLogs).toHaveBeenCalledTimes(2))
  })

  it('a non-write-role user never sees the "+ บันทึกวันนี้" button', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([])
    renderPage('Executive')

    await waitFor(() => expect(weatherApi.listWeatherLogs).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: '+ บันทึกวันนี้' })).not.toBeInTheDocument()
  })

  it('opens the correction modal targeting the clicked row', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([sampleLog])
    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' }))

    expect(screen.getByText(/แก้ไข\/ยกเลิกรายการ — บันทึกวันที่/)).toBeInTheDocument()
    // Pre-filled from the target row.
    expect(screen.getByLabelText(/ชั่วโมงที่หยุดงาน/)).toHaveValue(8)
  })

  it('a PM can evaluate EOT and see the relabelled result', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([sampleLog])
    vi.mocked(weatherApi.evaluateEot).mockResolvedValueOnce(sampleEvaluation)
    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('button', { name: 'ประเมิน EOT' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'ประเมิน EOT' }))

    await waitFor(() => expect(screen.getByTestId('eot-eligible-days')).toHaveTextContent('1'))
    expect(screen.getByText('ผลกระทบต่อกำหนดแล้วเสร็จ (EOT ที่ประเมินได้)')).toBeInTheDocument()
  })

  it('Site cannot evaluate EOT (narrower role gate than weather write)', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([])
    renderPage('Site')

    await waitFor(() => expect(weatherApi.listWeatherLogs).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: /ประเมิน EOT/ })).not.toBeInTheDocument()
  })

  it('the unattributed tile prompt filters the table down and can be cleared', async () => {
    const unattributed = { ...sampleLog, id: 'log-2', affectedActivityIds: [] }
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([sampleLog, unattributed])
    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '2'))

    await userEvent.click(screen.getByRole('button', { name: /ยื่นบันทึกแก้ไขเพื่อระบุกิจกรรม/ }))
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '1')

    await userEvent.click(screen.getByRole('button', { name: /ล้างตัวกรอง/ }))
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '2')
  })

  describe('S13-FE-01: offline queue + the correction-ordering problem', () => {
    it('shows a newly-queued entry in "คิวออฟไลน์ของอุปกรณ์นี้" while offline, without calling the API', async () => {
      vi.mocked(weatherApi.listWeatherLogs).mockResolvedValue([])
      renderPage('Site')

      await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึกวันนี้' })).toBeInTheDocument())
      await userEvent.click(screen.getByRole('button', { name: '+ บันทึกวันนี้' }))
      await userEvent.click(screen.getByRole('checkbox'))
      await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

      await waitFor(() => expect(screen.getByTestId('weather-outbox-item')).toBeInTheDocument())
      expect(screen.getByText('บันทึกใหม่')).toBeInTheDocument()
      expect(screen.getByText('รอซิงค์')).toBeInTheDocument()
      expect(weatherApi.recordWeatherLog).not.toHaveBeenCalled()
    })

    it('offers "แก้ไข/ยกเลิกรายการ" on a locally-queued (not yet synced) entry, and the resulting correction genuinely waits rather than failing opaquely', async () => {
      vi.mocked(weatherApi.listWeatherLogs).mockResolvedValue([])
      // The Original's own upload keeps rejecting once flushed — a realistic "still offline"/network
      // outcome, so it never reaches `synced`. That is exactly the "correction target hasn't synced
      // yet" scenario `weatherOutbox.ts` handles.
      vi.mocked(weatherApi.recordWeatherLog).mockRejectedValue(new weatherApi.WeatherApiError('เครือข่ายขัดข้อง'))
      renderPage('Site')

      // Queue the Original (stays `queued` — this test runs offline, `beforeEach`'s default).
      await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึกวันนี้' })).toBeInTheDocument())
      await userEvent.click(screen.getByRole('button', { name: '+ บันทึกวันนี้' }))
      await userEvent.click(screen.getByRole('checkbox'))
      await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

      // The queued (not yet synced) Original is offered a correction action, exactly like a
      // server-confirmed row would be.
      await waitFor(() => expect(screen.getByTestId('weather-outbox-item')).toBeInTheDocument())
      const correctButtons = screen.getAllByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' })
      await userEvent.click(correctButtons[correctButtons.length - 1])

      expect(screen.getByText(/แก้ไข\/ยกเลิกรายการ — บันทึกวันที่/)).toBeInTheDocument()
      await userEvent.click(screen.getByRole('checkbox'))
      await userEvent.type(screen.getByLabelText(/เหตุผลที่แก้ไข/), 'พิมพ์ผิด')
      await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ }))

      // Both items now queued (still offline) — the correction never called the API at all yet.
      await waitFor(() => expect(screen.getAllByTestId('weather-outbox-item')).toHaveLength(2))
      expect(weatherApi.recordWeatherLogCorrection).not.toHaveBeenCalled()

      // One manual sync processes both, oldest-first, in the same pass: the Original's own attempt
      // rejects (becomes `failed`), and the correction — reading the Original's now-updated, still-
      // unsynced state — genuinely waits, rather than either silently vanishing or throwing some
      // unrelated/opaque error.
      await userEvent.click(screen.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }))

      await waitFor(() => expect(weatherApi.recordWeatherLog).toHaveBeenCalledTimes(1))
      expect(weatherApi.recordWeatherLogCorrection).not.toHaveBeenCalled()
      await waitFor(() => expect(screen.getByText(/รอซิงค์บันทึกสภาพอากาศต้นฉบับให้เสร็จก่อน/)).toBeInTheDocument())
    })
  })
})
