import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { SCurveChart } from './SCurveChart'
import type { SCurvePoint } from './SCurveChart'

const historicalPoint: SCurvePoint = {
  dataDate: '2026-04-30T00:00:00+07:00',
  pv: '200000.00',
  ev: '180000.00',
  ac: '190000.00',
}

const livePoint: SCurvePoint = {
  dataDate: '2026-06-30T00:00:00+07:00',
  pv: '400000.00',
  ev: '300000.00',
  ac: '350000.00',
}

// `element.className` on an SVG element is an `SVGAnimatedString`, not a plain string — read the
// raw `class` attribute instead so this doesn't depend on jsdom's SVG DOM fidelity.
function classOf(el: Element): string {
  return el.getAttribute('class') ?? ''
}

function getPaths(container: HTMLElement) {
  return Array.from(container.querySelectorAll('path'))
}

describe('SCurveChart', () => {
  it('renders an accessible svg with role="img" and a descriptive aria-label', () => {
    render(<SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac="1166666.67" />)
    const svg = screen.getByRole('img')
    expect(svg.getAttribute('aria-label')).toContain('S-Curve')
    expect(svg.getAttribute('aria-label')).toContain('1,166,666.67')
  })

  it('draws PV as a dashed stroke-text-faint line, EV as a solid stroke-gold line, AC as a solid stroke-danger line', () => {
    const { container } = render(
      <SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac={null} />,
    )
    const paths = getPaths(container)

    const pvPath = paths.find((p) => classOf(p).includes('stroke-text-faint'))
    const evPath = paths.find((p) => classOf(p).includes('stroke-gold'))
    const acPath = paths.find((p) => classOf(p).includes('stroke-danger'))

    expect(pvPath).toBeDefined()
    expect(pvPath?.getAttribute('stroke-dasharray')).toBe('6 5')
    expect(evPath).toBeDefined()
    expect(evPath?.getAttribute('stroke-dasharray')).toBeNull()
    expect(acPath).toBeDefined()
    expect(acPath?.getAttribute('stroke-dasharray')).toBeNull()
  })

  it('draws the dashed EAC forecast line (stroke-secondary) when the selected variant is computable', () => {
    const { container } = render(
      <SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac="1166666.67" />,
    )
    const forecastPath = getPaths(container).find((p) => classOf(p).includes('stroke-secondary'))
    expect(forecastPath).toBeDefined()
    expect(forecastPath?.getAttribute('stroke-dasharray')).toBe('2 4')
  })

  it('DoD: draws NO forecast line when the selected variant is not computable (null EAC — honest, never a fabricated line)', () => {
    const { container } = render(
      <SCurveChart
        points={[historicalPoint, livePoint]}
        bac="1000000.00"
        forecastEac={null}
        forecastUnavailableReason="NoActualCost"
      />,
    )
    const forecastPath = getPaths(container).find((p) => classOf(p).includes('stroke-secondary'))
    expect(forecastPath).toBeUndefined()
    expect(screen.getByText(/ยังไม่แสดงเส้นพยากรณ์ EAC/)).toHaveTextContent('ยังไม่มีข้อมูลค่าใช้จ่ายจริง')
  })

  it('marks the live (last) EV/AC point with circles', () => {
    const { container } = render(
      <SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac={null} />,
    )
    const circles = container.querySelectorAll('circle')
    expect(circles).toHaveLength(2)
    expect(classOf(circles[0])).toContain('fill-gold')
    expect(classOf(circles[1])).toContain('fill-danger')
  })

  it('draws the navy dashed data-date line at the live point\'s time, labelled inside the chart itself', () => {
    render(<SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac={null} />)
    const svg = screen.getByRole('img')
    const dataDateLine = Array.from(svg.querySelectorAll('line')).find((l) => classOf(l).includes('stroke-navy'))
    expect(dataDateLine).toBeDefined()
    // Scoped to the svg itself — the caption paragraph below the chart also mentions "Data Date"
    // in its own explanatory sentence, so an unscoped query would match twice.
    expect(within(svg).getByText(/Data Date/)).toBeInTheDocument()
  })

  it('states plainly that pre-data-date points come from closed snapshots, not a live recomputation (ADR-0009)', () => {
    render(<SCurveChart points={[historicalPoint, livePoint]} bac="1000000.00" forecastEac={null} />)
    expect(screen.getByText(/EvmPeriodSnapshot/)).toBeInTheDocument()
  })

  it('handles a single point (brand-new project, no closed periods yet) without crashing', () => {
    const { container } = render(<SCurveChart points={[livePoint]} bac="1000000.00" forecastEac="1166666.67" />)
    expect(container.querySelectorAll('circle')).toHaveLength(2)
  })

  it('shows an honest empty state and no svg for zero points (defensive)', () => {
    render(<SCurveChart points={[]} bac="1000000.00" forecastEac={null} />)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('ยังไม่มีข้อมูลสำหรับวาดกราฟ S-Curve')).toBeInTheDocument()
  })
})
