import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { DashboardPage } from './DashboardPage'
import * as dashboardApi from './api'
import * as evmApi from '../evm/api'
import * as ganttApi from '../gantt/api'
import * as wbsApi from '../wbs/api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { GanttDto } from '../gantt'
import type { WbsTreeDto } from '../wbs'
import type { DashboardResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getDashboard: vi.fn() }
})
vi.mock('../evm/api', async () => {
  const actual = await vi.importActual<typeof import('../evm/api')>('../evm/api')
  return { ...actual, listEvmSnapshots: vi.fn() }
})
vi.mock('../gantt/api', async () => {
  const actual = await vi.importActual<typeof import('../gantt/api')>('../gantt/api')
  return { ...actual, getGantt: vi.fn() }
})
vi.mock('../wbs/api', async () => {
  const actual = await vi.importActual<typeof import('../wbs/api')>('../wbs/api')
  return { ...actual, getWbsTree: vi.fn() }
})

const sampleDashboard: DashboardResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pv: '285180000.00',
  ev: '262970000.00',
  ac: '253100000.00',
  sv: '-22210000.00',
  cv: '9870000.00',
  spi: '0.920000',
  cpi: '1.038995',
  actualCostEntryCount: 42,
  eacVariant: 'CpiBased',
  performanceFactor: '0.962477',
  etc: '213900000.00',
  eac: '466000000.00',
  vac: '19000000.00',
  eacComputable: true,
  eacNullReason: null,
  progressRollup: { progressPercentage: '54.20', weightWarnings: [], mixedScopeWbsNodeIds: [] },
  warnings: [],
}

const sampleGantt: GanttDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  activities: [
    {
      id: 'act-1',
      wbsNodeId: 'w1',
      activityCode: 'A-101',
      name: 'เทคอนกรีตฐานราก',
      plannedStart: '2026-01-01T00:00:00+07:00',
      plannedFinish: '2026-08-20T00:00:00+07:00',
      actualStart: null,
      actualFinish: null,
      isCritical: true,
      totalFloat: 0,
      freeFloat: 0,
    },
  ],
}

const sampleWbsTree: WbsTreeDto = {
  projectId: 'project-1',
  rootNodes: [
    { id: 'w1', parentWbsNodeId: null, code: 'W-01', title: 'โครงสร้าง', weightPercentage: '40.00', activityCount: 12, children: [] },
  ],
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
    <MemoryRouter initialEntries={['/app/project-1/dashboard']}>
      <Routes>
        <Route path="/app/:projectId/dashboard" element={<DashboardPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('DashboardPage (S8-FE-01 integration)', () => {
  beforeEach(() => {
    vi.mocked(dashboardApi.getDashboard).mockReset()
    vi.mocked(evmApi.listEvmSnapshots).mockReset()
    vi.mocked(ganttApi.getGantt).mockReset()
    vi.mocked(wbsApi.getWbsTree).mockReset()
    useAuthStore.getState().logout()
  })

  it('shows a loading state, then the real Dashboard screen (KPI row, S-Curve preview, critical-path preview, WBS rollup, photo placeholder) once data resolves', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValueOnce(sampleDashboard)
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValueOnce([])
    vi.mocked(ganttApi.getGantt).mockResolvedValueOnce(sampleGantt)
    vi.mocked(wbsApi.getWbsTree).mockResolvedValueOnce(sampleWbsTree)

    renderPage('PM')
    expect(screen.getByText('กำลังโหลดข้อมูล Dashboard...')).toBeInTheDocument()

    await waitFor(() => expect(screen.getByTestId('dashboard-page')).toBeInTheDocument())

    // Primary-response-driven content is synchronously ready as soon as `dashboard-page` mounts.
    expect(screen.getByTestId('dashboard-kpi-row').children).toHaveLength(6)
    expect(screen.getByText('EVM S-Curve')).toBeInTheDocument()
    expect(screen.getByText('Critical Path — งานวิกฤตใกล้ถึงกำหนด')).toBeInTheDocument()
    expect(screen.getByText('ความก้าวหน้าตาม WBS (ถ่วงน้ำหนัก)')).toBeInTheDocument()
    expect(screen.getByText('Photo Progress ล่าสุด')).toBeInTheDocument()
    expect(screen.getByText(/Photo Progress อยู่ระหว่างพัฒนา \(Sprint 12\)/)).toBeInTheDocument()

    // These three come from independent secondary fetches (`useGanttData`/`useWbsTree`) that may
    // settle on a later tick than the primary `useDashboardData` — `findBy*` retries instead of
    // assuming they are already resolved the instant `dashboard-page` first appears.
    expect(await screen.findByText('เทคอนกรีตฐานราก')).toBeInTheDocument() // real critical activity from Gantt
    expect(await screen.findByText('W-01')).toBeInTheDocument() // real WBS branch
  })

  it('shows a Thai error state on the primary load failure, never a blank/broken screen', async () => {
    vi.mocked(dashboardApi.getDashboard).mockRejectedValueOnce(new dashboardApi.DashboardApiError('ไม่พบโครงการที่ระบุ', 404))
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValueOnce([])
    vi.mocked(ganttApi.getGantt).mockResolvedValueOnce(sampleGantt)
    vi.mocked(wbsApi.getWbsTree).mockResolvedValueOnce(sampleWbsTree)

    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ'))
  })

  it('role gate (ADR-0013): PM sees the real screen; Site sees 403 and the dashboard endpoint is never even called', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(sampleDashboard)
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValue([])
    vi.mocked(ganttApi.getGantt).mockResolvedValue(sampleGantt)
    vi.mocked(wbsApi.getWbsTree).mockResolvedValue(sampleWbsTree)

    const { unmount } = renderPage('Site')
    expect(await screen.findByText('403')).toBeInTheDocument()
    expect(screen.getByText('ไม่มีสิทธิ์เข้าถึงหน้านี้')).toBeInTheDocument()
    // Not just the primary read — the whole screen (and its 3 secondary fetches) never mounts for a
    // disallowed role, so none of the 4 endpoints this screen calls are ever hit.
    expect(dashboardApi.getDashboard).not.toHaveBeenCalled()
    expect(evmApi.listEvmSnapshots).not.toHaveBeenCalled()
    expect(ganttApi.getGantt).not.toHaveBeenCalled()
    expect(wbsApi.getWbsTree).not.toHaveBeenCalled()
    unmount()

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('dashboard-page')).toBeInTheDocument())
    expect(dashboardApi.getDashboard).toHaveBeenCalledTimes(1)
  })

  it("today's real gap (AC=0, EAC not computable — no cost-recording activity on most projects) renders honest reason text, never a fabricated number", async () => {
    const noActualCost: DashboardResponseDto = {
      ...sampleDashboard,
      ac: '0.00',
      cv: sampleDashboard.ev, // EV - AC with AC=0.00 -> CV = EV
      cpi: null,
      eac: null,
      vac: null,
      etc: null,
      performanceFactor: null,
      eacComputable: false,
      eacNullReason: 'NoActualCost',
      actualCostEntryCount: 0,
    }
    vi.mocked(dashboardApi.getDashboard).mockResolvedValueOnce(noActualCost)
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValueOnce([])
    vi.mocked(ganttApi.getGantt).mockResolvedValueOnce(sampleGantt)
    vi.mocked(wbsApi.getWbsTree).mockResolvedValueOnce(sampleWbsTree)

    renderPage('PM')

    await waitFor(() => expect(screen.getByTestId('dashboard-page')).toBeInTheDocument())
    expect(screen.getAllByText(/ยังไม่มีข้อมูลค่าใช้จ่ายจริง/).length).toBeGreaterThan(0)
  })

  it('a secondary widget failure (Gantt down) degrades only the critical-path preview — the rest of the dashboard still renders', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValueOnce(sampleDashboard)
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValueOnce([])
    vi.mocked(ganttApi.getGantt).mockRejectedValueOnce(new ganttApi.GanttApiError('โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'))
    vi.mocked(wbsApi.getWbsTree).mockResolvedValueOnce(sampleWbsTree)

    renderPage('PM')

    await waitFor(() => expect(screen.getByTestId('dashboard-kpi-row')).toBeInTheDocument())
    expect(await screen.findByText('โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง')).toBeInTheDocument()
    // The rest of the page is unaffected — still real, still rendered. 54.20% legitimately appears
    // twice (the KPI tile and the WBS rollup card headline both read the same `progressRollup`
    // figure — the DoD's own "rollup ตรงกับหน้า WBS" consistency requirement, in miniature).
    expect(await screen.findByText('W-01')).toBeInTheDocument()
    expect(screen.getAllByText('54.20%')).toHaveLength(2)
  })

  it('surfaces EVM-engine warnings on the dashboard (never silently dropped)', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValueOnce({
      ...sampleDashboard,
      warnings: ['EarnedValueExceedsBudget'],
    })
    vi.mocked(evmApi.listEvmSnapshots).mockResolvedValueOnce([])
    vi.mocked(ganttApi.getGantt).mockResolvedValueOnce(sampleGantt)
    vi.mocked(wbsApi.getWbsTree).mockResolvedValueOnce(sampleWbsTree)

    renderPage('PM')

    await waitFor(() => expect(screen.getByText(/เกินงบประมาณ/)).toBeInTheDocument())
  })
})
