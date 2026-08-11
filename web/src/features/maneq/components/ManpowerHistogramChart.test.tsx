import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ManpowerHistogramChart } from './ManpowerHistogramChart'
import type { HistogramPoint } from '../useManpowerOverview'
import type { ProductivityIndexResponseDto } from '../types'

function makePoint(dateInputValue: string, overrides: Partial<ProductivityIndexResponseDto> = {}): HistogramPoint {
  return {
    dateInputValue,
    response: {
      projectId: 'project-1',
      wbsNodeId: null,
      activityId: null,
      from: `${dateInputValue}T00:00:00.000Z`,
      to: `${dateInputValue}T00:00:00.000Z`,
      productivityIndex: '0.90',
      productivityIndexNullReason: null,
      earnedManHours: '180.00',
      actualManHoursInScope: '200.00',
      actualManHoursTotal: '200.00',
      excludedManHours: '0.00',
      coveragePercentage: '100.00',
      logEntryCount: 1,
      warnings: [],
      manningRatio: '1.00',
      actualWorkerCount: 25,
      plannedWorkerCount: 25,
      ...overrides,
    },
  }
}

describe('ManpowerHistogramChart', () => {
  it('shows an empty-state message when there are no points', () => {
    render(<ManpowerHistogramChart points={[]} />)
    expect(screen.getByText('ยังไม่มีข้อมูลสำหรับวาด Histogram')).toBeInTheDocument()
  })

  it('renders one bar per point plus a legend explaining the colours', () => {
    const points = [makePoint('2026-07-05'), makePoint('2026-07-06'), makePoint('2026-07-07')]
    render(<ManpowerHistogramChart points={points} />)

    expect(screen.getByRole('img', { name: /Histogram กำลังคน 3 วันล่าสุด/ })).toBeInTheDocument()
    expect(screen.getByText('ต่ำกว่าแผน')).toBeInTheDocument()
    expect(screen.getByText('สูงกว่าแผน')).toBeInTheDocument()
  })

  it('never colours an over-manning bar green — the fixed prototype defect stays fixed at the chart level too', () => {
    const points = [makePoint('2026-07-05', { actualWorkerCount: 230, plannedWorkerCount: 200 })]
    const { container } = render(<ManpowerHistogramChart points={points} />)
    const bar = container.querySelector('rect')
    expect(bar).not.toBeNull()
    expect(bar?.getAttribute('class')).not.toMatch(/fill-success/)
    expect(bar?.getAttribute('class')).toMatch(/fill-gold/)
  })

  it('renders a hollow marker (not a numeric dot) for a null PI day, never plotting it as 0', () => {
    const points = [makePoint('2026-07-05', { productivityIndex: null, productivityIndexNullReason: 'NotReported' })]
    const { container } = render(<ManpowerHistogramChart points={points} />)
    const hollowMarker = container.querySelector('circle.fill-surface')
    expect(hollowMarker).not.toBeNull()
  })
})
