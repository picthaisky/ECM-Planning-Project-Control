import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
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
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(weatherApi.listWeatherLogs).mockReset()
    vi.mocked(weatherApi.recordWeatherLog).mockReset()
    vi.mocked(weatherApi.recordWeatherLogCorrection).mockReset()
    vi.mocked(weatherApi.evaluateEot).mockReset()
  })

  it('loads the register and shows the tiles + table populated from it', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValueOnce([sampleLog])
    renderPage()

    // Both the rain-day and stoppage-day tiles read "1 วัน" for this single fixture entry.
    await waitFor(() => expect(screen.getAllByText('1 วัน')).toHaveLength(2))
    expect(screen.getByText('หยุดงานภายนอกทั้งวัน')).toBeInTheDocument() // table row
  })

  it('a write-role user can open the record modal and submit a new entry', async () => {
    vi.mocked(weatherApi.listWeatherLogs).mockResolvedValue([])
    vi.mocked(weatherApi.recordWeatherLog).mockResolvedValueOnce(sampleLog)
    renderPage('Site')

    await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึกวันนี้' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: '+ บันทึกวันนี้' }))

    expect(screen.getByText('บันทึกสภาพอากาศไม่สามารถแก้ไขหรือลบได้หลังบันทึก')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    await waitFor(() => expect(weatherApi.recordWeatherLog).toHaveBeenCalledWith('project-1', expect.any(Object)))
    // reload() re-fetches after a successful record — asserted by a second listWeatherLogs call.
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
})
