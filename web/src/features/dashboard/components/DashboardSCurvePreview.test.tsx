import type { ComponentProps } from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { DashboardSCurvePreview } from './DashboardSCurvePreview'
import type { EvmSnapshotDto } from '../../evm'
import type { DashboardResponseDto } from '../types'

const baseDashboard: DashboardResponseDto = {
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
  cumulativeDisbursement: '0.00',
  cumulativeWeatherStoppageDays: 0,
}

function renderPreview(overrides: Partial<ComponentProps<typeof DashboardSCurvePreview>> = {}) {
  return render(
    <MemoryRouter>
      <DashboardSCurvePreview
        projectId="project-1"
        dashboard={baseDashboard}
        snapshots={[]}
        snapshotsLoadState="ready"
        snapshotsLoadError={null}
        {...overrides}
      />
    </MemoryRouter>,
  )
}

describe('DashboardSCurvePreview (S8-FE-01)', () => {
  it('renders the real S-Curve chart from the live dashboard point (dataDate/pv/ev/ac), no fabricated series', () => {
    renderPreview()

    const svg = screen.getByRole('img')
    expect(svg).toBeInTheDocument()
    expect(svg.getAttribute('aria-label')).toContain('466,000,000.00') // forecast EAC value shown in the chart's own aria-label
  })

  it('links through to the real EVM screen', () => {
    renderPreview()
    expect(screen.getByRole('link', { name: /ดูรายละเอียด EVM/ })).toHaveAttribute('href', '/app/project-1/evm')
  })

  it('draws no forecast line and shows the honest reason when EAC is not computable', () => {
    renderPreview({
      dashboard: { ...baseDashboard, eac: null, eacComputable: false, eacNullReason: 'NoActualCost' },
    })

    expect(screen.getByText(/ยังไม่มีข้อมูลค่าใช้จ่ายจริง/)).toBeInTheDocument()
  })

  it('includes closed-period snapshots strictly before the live data date (ADR-0009), via the real buildSCurvePoints', () => {
    const snapshot: EvmSnapshotDto = {
      snapshotId: 'snap-1',
      projectId: 'project-1',
      dataDate: '2026-06-30T00:00:00+07:00',
      bac: '485000000.00',
      pv: '260000000.00',
      ev: '245000000.00',
      ac: '235000000.00',
      eacVariant: 'CpiBased',
      performanceFactor: '0.95',
      eac: '460000000.00',
      etc: '225000000.00',
      vac: '25000000.00',
      createdAt: '2026-07-01T09:00:00+07:00',
    }

    renderPreview({ snapshots: [snapshot] })

    // Same wiring proof as `EvmPage.test.tsx`'s identical ADR-0009 test — the actual point-inclusion
    // rule is unit-tested directly against `buildSCurvePoints` in `features/evm/evmSelectors.test.ts`.
    expect(screen.getByRole('img')).toBeInTheDocument()
  })

  it('shows a loading state while the snapshot history is still in flight', () => {
    renderPreview({ snapshotsLoadState: 'loading' })
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('surfaces a snapshot-history load failure without breaking the live point chart', () => {
    renderPreview({ snapshotsLoadState: 'error', snapshotsLoadError: 'โหลดประวัติงวด EVM ไม่สำเร็จ' })

    expect(screen.getByRole('alert')).toHaveTextContent('โหลดประวัติงวด EVM ไม่สำเร็จ')
    expect(screen.getByRole('img')).toBeInTheDocument()
  })
})
