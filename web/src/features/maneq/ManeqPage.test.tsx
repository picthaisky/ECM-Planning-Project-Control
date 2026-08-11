import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ManeqPage } from './ManeqPage'
import * as maneqApi from './api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { ManpowerLogDto, ProductivityIndexResponseDto } from './types'

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
    getProductivityIndex: vi.fn(),
    recordManpowerLog: vi.fn(),
    recordManpowerLogCorrection: vi.fn(),
  }
})

function samplePiResponse(overrides: Partial<ProductivityIndexResponseDto> = {}): ProductivityIndexResponseDto {
  return {
    projectId: 'project-1',
    wbsNodeId: null,
    activityId: null,
    from: null,
    to: '2026-07-08T00:00:00.000Z',
    productivityIndex: '0.90',
    productivityIndexNullReason: null,
    earnedManHours: '180.00',
    actualManHoursInScope: '200.00',
    actualManHoursTotal: '200.00',
    excludedManHours: '0.00',
    coveragePercentage: '100.00',
    logEntryCount: 1,
    warnings: [],
    manningRatio: null,
    actualWorkerCount: null,
    plannedWorkerCount: null,
    ...overrides,
  }
}

const sampleLog: ManpowerLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  logDate: '2026-07-08T00:00:00.000Z',
  shift: 'Day',
  workCategoryId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
  wbsNodeId: null,
  activityId: null,
  labourType: 'OwnDirect',
  subcontractorRef: null,
  workerCount: 25,
  manHours: '200.00',
  overtimeHours: '0.00',
  manHoursDerived: false,
  equipmentCount: 0,
  equipmentOperatingHours: '0.00',
  equipmentStandbyHours: '0.00',
  workDescription: null,
  relatedWeatherLogId: null,
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-08T09:00:00.000Z',
  entryKind: 'Original',
  correctsLogId: null,
  correctionReason: null,
  allowDuplicateOverride: false,
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
    <MemoryRouter initialEntries={['/app/project-1/maneq']}>
      <Routes>
        <Route path="/app/:projectId/maneq" element={<ManeqPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ManeqPage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(maneqApi.getProductivityIndex).mockReset()
    vi.mocked(maneqApi.recordManpowerLog).mockReset()
    vi.mocked(maneqApi.recordManpowerLogCorrection).mockReset()
    vi.mocked(maneqApi.getProductivityIndex).mockResolvedValue(samplePiResponse())
  })

  it('loads and shows the KPI tiles and histogram from real productivity-index data', async () => {
    renderPage()
    await waitFor(() => expect(maneqApi.getProductivityIndex).toHaveBeenCalled())
    await waitFor(() => expect(screen.getByText('0.90')).toBeInTheDocument())
    expect(screen.getByText('Productivity Index')).toBeInTheDocument()
  })

  it('a write-role user can open the record modal, submit, and see the row appear in the session table', async () => {
    vi.mocked(maneqApi.recordManpowerLog).mockResolvedValueOnce(sampleLog)
    renderPage('Site')

    await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึกวันนี้' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: '+ บันทึกวันนี้' }))

    await userEvent.type(screen.getByLabelText(/Work Category ID/), sampleLog.workCategoryId)
    await userEvent.clear(screen.getByLabelText('จำนวนคน (Worker Count)'))
    await userEvent.type(screen.getByLabelText('จำนวนคน (Worker Count)'), '25')
    await userEvent.clear(screen.getByLabelText('ชั่วโมงแรงงานรวม (Man-Hours)'))
    await userEvent.type(screen.getByLabelText('ชั่วโมงแรงงานรวม (Man-Hours)'), '200')
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    await waitFor(() => expect(maneqApi.recordManpowerLog).toHaveBeenCalledWith('project-1', expect.any(Object)))
    // The session-local table (no GET list endpoint exists) shows the freshly-recorded row.
    await waitFor(() => expect(screen.getByText('25')).toBeInTheDocument())
  })

  it('a non-write-role user never sees the "+ บันทึกวันนี้" button', async () => {
    renderPage('Executive')
    await waitFor(() => expect(maneqApi.getProductivityIndex).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: '+ บันทึกวันนี้' })).not.toBeInTheDocument()
  })

  it('states plainly that the table is session-local (no historical register endpoint yet)', async () => {
    renderPage()
    expect(screen.getByText(/ระบบหลังบ้านยังไม่มี endpoint สำหรับดึงประวัติทั้งหมด/)).toBeInTheDocument()
  })

  it('shows the "—" + reason copy for a null cumulative PI, never a bare dash alone', async () => {
    vi.mocked(maneqApi.getProductivityIndex).mockImplementation(async (_projectId, params) => {
      if (params.from === null || params.from === undefined) {
        return samplePiResponse({ productivityIndex: null, productivityIndexNullReason: 'NoBudgetManHours' })
      }
      return samplePiResponse()
    })

    renderPage()
    await waitFor(() => expect(screen.getByText(/ยังไม่ได้ประมาณการเป็นชั่วโมง-คน/)).toBeInTheDocument())
  })
})
