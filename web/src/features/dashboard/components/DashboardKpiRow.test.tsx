import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { DashboardKpiRow } from './DashboardKpiRow'
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
}

function renderRow(dashboard: DashboardResponseDto) {
  return render(
    <MemoryRouter>
      <DashboardKpiRow projectId="project-1" dashboard={dashboard} />
    </MemoryRouter>,
  )
}

describe('DashboardKpiRow (S8-FE-01)', () => {
  it('renders exactly 6 tiles in the prototype order, each carrying a formula/explanation caption', () => {
    renderRow(baseDashboard)

    const row = screen.getByTestId('dashboard-kpi-row')
    expect(row.children).toHaveLength(6)

    expect(screen.getByText('ความก้าวหน้า (Actual)')).toBeInTheDocument()
    expect(screen.getByText('SPI')).toBeInTheDocument()
    expect(screen.getByText('CPI')).toBeInTheDocument()
    expect(screen.getByText('EAC')).toBeInTheDocument()
    expect(screen.getByText('เบิกจ่ายสะสม')).toBeInTheDocument()
    expect(screen.getByText('วันฝนตกสะสม')).toBeInTheDocument()
  })

  it('renders the real progress/SPI/CPI/EAC figures, formatted per convention (percent 2dp, ratio 2dp, MB for money)', () => {
    renderRow(baseDashboard)

    expect(screen.getByText('54.20%')).toBeInTheDocument() // progress
    expect(screen.getByText('0.92')).toBeInTheDocument() // SPI
    expect(screen.getByText('1.04')).toBeInTheDocument() // CPI
    expect(screen.getByText('466.00 MB')).toBeInTheDocument() // EAC
  })

  it('DoD: every tile carries a formula caption, e.g. the EAC tile shows "BAC / CPI (MB)"', () => {
    renderRow(baseDashboard)
    expect(screen.getByText('BAC / CPI (MB)')).toBeInTheDocument()
  })

  it('shows the EAC tile VAC sub-line colored by sign (favourable/green here, matches the prototype "ต่ำกว่างบ" mock)', () => {
    renderRow(baseDashboard)
    const vacLine = screen.getByText(/VAC \+19\.00 MB/)
    expect(vacLine).toBeInTheDocument()
    expect(vacLine).toHaveTextContent('ต่ำกว่างบ')
    expect(vacLine.className).toContain('text-success')
  })

  it('honest-null: SPI/CPI/EAC render "—" (never 0) when the backend returns null, with the backend\'s own reason as the EAC caption', () => {
    const notStarted: DashboardResponseDto = {
      ...baseDashboard,
      spi: null,
      cpi: null,
      eac: null,
      vac: null,
      eacComputable: false,
      eacNullReason: 'NotStarted',
    }
    renderRow(notStarted)

    const dashes = screen.getAllByText('—')
    expect(dashes.length).toBeGreaterThanOrEqual(3) // SPI, CPI, EAC tiles
    expect(screen.getByText(/โครงการยังไม่เริ่มดำเนินการ/)).toBeInTheDocument()
  })

  it('scope honesty: the disbursement and rain-day tiles never show a fabricated number, always "—" plus an explicit unavailability reason', () => {
    renderRow(baseDashboard)

    expect(screen.getByText(/รอ Payment Certificate \(Sprint 9\)/)).toBeInTheDocument()
    expect(screen.getByText(/Weather Log ยังไม่เปิดใช้งาน/)).toBeInTheDocument()
  })

  it('every tile is a real link to the screen it drills into (prototype onClick affordance)', () => {
    renderRow(baseDashboard)

    const links = screen.getAllByRole('link')
    const hrefs = links.map((link) => link.getAttribute('href'))
    expect(hrefs).toEqual([
      '/app/project-1/wbs',
      '/app/project-1/evm',
      '/app/project-1/evm',
      '/app/project-1/evm',
      '/app/project-1/payment',
      '/app/project-1/weather',
    ])
  })
})
