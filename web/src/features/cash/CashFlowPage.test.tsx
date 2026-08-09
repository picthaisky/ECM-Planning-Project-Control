import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CashFlowPage } from './CashFlowPage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { CashFlowResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getCashFlow: vi.fn() }
})

const sampleCashFlow: CashFlowResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pvCumulative: '285180000.00',
  evCumulative: '262970000.00',
  acCumulative: '253100000.00',
  actualCostEntryCount: 42,
  periods: [
    {
      periodStart: '2026-05-31T00:00:00+07:00',
      periodEnd: '2026-06-30T00:00:00+07:00',
      isClosed: true,
      pvPeriod: '25000000.00',
      evPeriod: '23000000.00',
      acPeriod: '22000000.00',
      pvCumulative: '260000000.00',
      evCumulative: '245000000.00',
      acCumulative: '234100000.00',
    },
    {
      periodStart: '2026-06-30T00:00:00+07:00',
      periodEnd: '2026-07-11T00:00:00+07:00',
      isClosed: false,
      pvPeriod: '25180000.00',
      evPeriod: '17970000.00',
      acPeriod: '19000000.00',
      pvCumulative: '285180000.00',
      evCumulative: '262970000.00',
      acCumulative: '253100000.00',
    },
  ],
  receipts: {
    isAvailable: false,
    cumulative: null,
    periods: [],
    unavailableReason: 'PaymentCertificatesNotYetImplemented',
  },
  netCashPosition: null,
  warnings: [],
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
    <MemoryRouter initialEntries={['/app/project-1/cash']}>
      <Routes>
        <Route path="/app/:projectId/cash" element={<CashFlowPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('CashFlowPage (S8-FE-02 integration)', () => {
  beforeEach(() => {
    vi.mocked(api.getCashFlow).mockReset()
    useAuthStore.getState().logout()
  })

  it('shows a loading state, then the real Cash Flow screen (bar chart + 4 summary tiles) once data resolves', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce(sampleCashFlow)

    renderPage('PM')
    expect(screen.getByText('กำลังโหลดข้อมูล Cash Flow...')).toBeInTheDocument()

    await waitFor(() => expect(screen.getByTestId('cash-flow-page')).toBeInTheDocument())

    expect(screen.getByText('กระแสเงินสดรายงวด (Cash Flow)')).toBeInTheDocument()
    expect(screen.getByRole('img')).toBeInTheDocument() // the bar chart svg
    expect(screen.getByTestId('cash-flow-summary-tiles').children).toHaveLength(4)
    expect(screen.getByText('253.10 MB')).toBeInTheDocument() // real AC figure
  })

  it('shows a Thai error state on load failure, never a blank/broken screen', async () => {
    vi.mocked(api.getCashFlow).mockRejectedValueOnce(new api.CashFlowApiError('ไม่พบโครงการที่ระบุ', 404))

    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ'))
  })

  it('role gate (ADR-0013): PM sees the real screen; Site sees 403 and the cash-flow endpoint is never even called', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValue(sampleCashFlow)

    const { unmount } = renderPage('Site')
    expect(await screen.findByText('403')).toBeInTheDocument()
    expect(api.getCashFlow).not.toHaveBeenCalled()
    unmount()

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('cash-flow-page')).toBeInTheDocument())
    expect(api.getCashFlow).toHaveBeenCalledTimes(1)
  })

  it("today's real gap: receipts unavailable and Net Cash Position null render as honest \"not available\" states, never fabricated numbers", async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce(sampleCashFlow)

    renderPage('PM')

    await waitFor(() => expect(screen.getByTestId('cash-flow-page')).toBeInTheDocument())
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2) // receipts + Net Cash Position
    // Both the receipts and Retention tiles independently cite Sprint 9 as their own reason.
    expect(screen.getAllByText(/Sprint 9/).length).toBeGreaterThanOrEqual(2)
  })

  it('surfaces a PeriodRestated warning rather than swallowing it', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce({ ...sampleCashFlow, warnings: ['CashFlowPeriodRestated'] })

    renderPage('PM')

    await waitFor(() => expect(screen.getByText(/Restated/)).toBeInTheDocument())
  })

  it('most-projects-sparse case (task note #3): a single live-only period bucket still renders a well-formed chart, not a broken one', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce({
      ...sampleCashFlow,
      periods: [sampleCashFlow.periods[1]], // only the trailing live bucket
    })

    renderPage('PM')

    await waitFor(() => expect(screen.getByTestId('cash-flow-page')).toBeInTheDocument())
    const svg = screen.getByRole('img')
    expect(svg.getAttribute('aria-label')).toContain('1 งวด')
  })
})
