import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { BaselinePage } from './BaselinePage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { BaselineComparisonDto, BaselineDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    listBaselines: vi.fn(),
    captureBaseline: vi.fn(),
    activateBaseline: vi.fn(),
    compareBaseline: vi.fn(),
  }
})

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 480 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 1000 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

const activeBaseline: BaselineDto = {
  id: 'baseline-1',
  projectId: 'project-1',
  name: 'Baseline 1 - อนุมัติสัญญา',
  isActive: true,
  capturedAt: '2026-07-01T00:00:00+07:00',
  capturedByUserId: 'user-1',
  bac: '1000000.00',
  activityCount: 2,
}

const comparison: BaselineComparisonDto = {
  projectId: 'project-1',
  baselineId: 'baseline-1',
  baselineName: 'Baseline 1 - อนุมัติสัญญา',
  baselineCapturedAt: '2026-07-01T00:00:00+07:00',
  totalActivityCount: 2,
  driftedActivityCount: 1,
  projectFinishVarianceDays: 3,
  currentBac: '1000000.00',
  baselineBac: '1000000.00',
  bacVarianceAmount: '0.00',
  activities: [
    {
      activityId: 'act-214',
      activityCode: 'ACT-214',
      name: 'งานโครงสร้างชั้น 4',
      isRemoved: false,
      baselinePlannedStart: '2026-06-29T00:00:00+07:00',
      baselinePlannedFinish: '2026-07-23T00:00:00+07:00',
      baselineDurationDays: 24,
      baselineBudgetCost: '2000000.00',
      currentPlannedStart: '2026-06-30T00:00:00+07:00',
      currentPlannedFinish: '2026-07-26T00:00:00+07:00',
      currentDurationDays: 26,
      currentBudgetCost: '2100000.00',
      isCritical: true,
      startVarianceDays: 1,
      finishVarianceDays: 3,
      durationVarianceDays: 2,
      budgetVarianceAmount: '100000.00',
    },
  ],
}

function loginAs(role: 'PM' | 'Planning' | 'QS' | 'Site' | 'Admin') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/app/project-1/baseline']}>
      <Routes>
        <Route path="/app/:projectId/baseline" element={<BaselinePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('BaselinePage (S14-FE-01)', () => {
  beforeEach(() => {
    vi.mocked(api.listBaselines).mockReset()
    vi.mocked(api.captureBaseline).mockReset()
    vi.mocked(api.activateBaseline).mockReset()
    vi.mocked(api.compareBaseline).mockReset()
    useAuthStore.getState().logout()
  })

  it('renders the summary tiles and the comparison table from the real, live compare endpoint', async () => {
    loginAs('PM')
    vi.mocked(api.listBaselines).mockResolvedValueOnce([activeBaseline])
    vi.mocked(api.compareBaseline).mockResolvedValueOnce(comparison)

    renderPage()

    await waitFor(() => expect(screen.getByText('ACT-214')).toBeInTheDocument())
    expect(screen.getByText('1 / 2')).toBeInTheDocument() // driftedActivityCount / totalActivityCount
    expect(screen.getByText(/vs Baseline 1 - อนุมัติสัญญา/)).toBeInTheDocument()
  })

  it('shows the "no active baseline" state instead of a generic error when BaselineNoActiveBaseline is returned', async () => {
    loginAs('PM')
    vi.mocked(api.listBaselines).mockResolvedValueOnce([])
    vi.mocked(api.compareBaseline).mockRejectedValueOnce(
      new api.BaselineApiError('โครงการนี้ยังไม่มี Baseline ที่ Active', 422, api.BASELINE_NO_ACTIVE_BASELINE_CODE),
    )

    renderPage()

    await waitFor(() =>
      expect(screen.getByText(/บันทึกและตั้งเป็น Active อย่างน้อยหนึ่งชุดก่อน/)).toBeInTheDocument(),
    )
  })

  it('DoD: capturing a baseline (PM role) adds it to the session list; a Site user sees no write affordances at all', async () => {
    loginAs('Site')
    vi.mocked(api.listBaselines).mockResolvedValueOnce([])
    vi.mocked(api.compareBaseline).mockRejectedValueOnce(
      new api.BaselineApiError('x', 422, api.BASELINE_NO_ACTIVE_BASELINE_CODE),
    )

    const { unmount } = renderPage()
    await waitFor(() => expect(screen.getByText('ยังไม่มี Baseline ที่สร้างในเซสชันนี้')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: '+ บันทึก Baseline ใหม่' })).not.toBeInTheDocument()
    unmount()

    loginAs('PM')
    vi.mocked(api.listBaselines).mockResolvedValueOnce([])
    vi.mocked(api.compareBaseline).mockResolvedValueOnce({ ...comparison, activities: [] })
    const created: BaselineDto = { ...activeBaseline, isActive: false }
    vi.mocked(api.captureBaseline).mockResolvedValueOnce(created)
    const user = userEvent.setup()

    renderPage()
    await waitFor(() => expect(screen.getByRole('button', { name: '+ บันทึก Baseline ใหม่' })).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: '+ บันทึก Baseline ใหม่' }))
    await user.type(screen.getByLabelText('ชื่อ Baseline'), created.name)
    await user.click(screen.getByRole('button', { name: 'บันทึก Baseline' }))

    await waitFor(() => expect(api.captureBaseline).toHaveBeenCalledWith('project-1', created.name))
    await waitFor(() => expect(screen.getByText(created.name)).toBeInTheDocument())
  })

  it('activating a baseline reloads the comparison table so it reflects the newly-active one', async () => {
    loginAs('PM')
    vi.mocked(api.listBaselines).mockResolvedValueOnce([{ ...activeBaseline, isActive: false }])
    vi.mocked(api.compareBaseline)
      .mockRejectedValueOnce(new api.BaselineApiError('x', 422, api.BASELINE_NO_ACTIVE_BASELINE_CODE))
      .mockResolvedValueOnce(comparison)
    vi.mocked(api.activateBaseline).mockResolvedValueOnce({
      id: 'baseline-1',
      projectId: 'project-1',
      isActive: true,
    })
    const user = userEvent.setup()

    renderPage()
    await waitFor(() => expect(screen.getByRole('button', { name: 'ตั้งเป็น Active' })).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'ตั้งเป็น Active' }))

    await waitFor(() => expect(api.compareBaseline).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.getByText('ACT-214')).toBeInTheDocument())
  })
})
