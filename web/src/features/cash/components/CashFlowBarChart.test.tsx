import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CashFlowBarChart } from './CashFlowBarChart'
import type { CashFlowPeriodPointDto } from '../types'

function period(overrides: Partial<CashFlowPeriodPointDto>): CashFlowPeriodPointDto {
  return {
    periodStart: '2026-06-01T00:00:00+07:00',
    periodEnd: '2026-06-30T00:00:00+07:00',
    isClosed: true,
    pvPeriod: '20000000.00',
    evPeriod: '18000000.00',
    acPeriod: '19000000.00',
    pvCumulative: '260000000.00',
    evCumulative: '245000000.00',
    acCumulative: '235000000.00',
    ...overrides,
  }
}

describe('CashFlowBarChart (S8-FE-02)', () => {
  it('honest empty state when there are no periods at all, never a broken/blank chart', () => {
    render(<CashFlowBarChart periods={[]} />)
    expect(screen.getByText('ยังไม่มีข้อมูลงวดสำหรับวาดกราฟ Cash Flow')).toBeInTheDocument()
  })

  it('renders a single well-formed cluster for a sparse chart (task note #3 — "essentially one bar" is the intended design)', () => {
    render(<CashFlowBarChart periods={[period({})]} />)

    const svg = screen.getByRole('img')
    expect(svg).toBeInTheDocument()
    expect(svg.getAttribute('aria-label')).toContain('1 งวด')
    // 3 bars (PV/EV/AC) for the one period.
    expect(document.querySelectorAll('rect')).toHaveLength(3)
  })

  it('renders 3 bars per period, for every period', () => {
    render(
      <CashFlowBarChart
        periods={[
          period({ periodEnd: '2026-05-31T00:00:00+07:00' }),
          period({ periodEnd: '2026-06-30T00:00:00+07:00' }),
          period({ periodEnd: '2026-07-11T00:00:00+07:00', isClosed: false }),
        ]}
      />,
    )

    expect(document.querySelectorAll('rect')).toHaveLength(9)
  })

  it('the trailing live (not-yet-closed) bucket is visually distinguished (reduced opacity) from closed periods', () => {
    render(
      <CashFlowBarChart
        periods={[
          period({ periodEnd: '2026-06-30T00:00:00+07:00', isClosed: true }),
          period({ periodEnd: '2026-07-11T00:00:00+07:00', isClosed: false }),
        ]}
      />,
    )

    const groups = document.querySelectorAll('g[opacity]')
    const opacities = Array.from(groups).map((g) => g.getAttribute('opacity'))
    expect(opacities).toEqual(['1', '0.6'])
  })

  it('exact period values are available on hover via a real <title> element (accessible, not lost to the compact MB display)', () => {
    render(<CashFlowBarChart periods={[period({ acPeriod: '19000000.00' })]} />)

    const titles = Array.from(document.querySelectorAll('rect title')).map((t) => t.textContent)
    expect(titles.some((t) => t?.includes('AC') && t.includes('19.00 MB'))).toBe(true)
  })

  it('never plots a receipts series — there is no such prop, by construction (ADR-0013 §5)', () => {
    // Type-level guarantee: `CashFlowBarChartProps` has no `receipts` field at all. This test
    // documents that guarantee for a human reader; the real enforcement is TypeScript itself.
    render(<CashFlowBarChart periods={[period({})]} />)
    expect(screen.queryByText(/รับเงิน/)).not.toBeInTheDocument()
  })
})
