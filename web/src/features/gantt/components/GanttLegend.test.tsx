import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { GanttLegend } from './GanttLegend'

describe('GanttLegend', () => {
  it('renders the critical/non-critical/baseline swatches matching the prototype (docs/ECM Planning Prototype.dc.html #4)', () => {
    render(<GanttLegend />)

    expect(screen.getByText('Critical (TF=0)')).toBeInTheDocument()
    expect(screen.getByText('Non-critical')).toBeInTheDocument()
    expect(screen.getByText('Baseline')).toBeInTheDocument()
  })

  it('flags the Baseline swatch as having no real data yet, rather than implying it does', () => {
    render(<GanttLegend />)
    expect(screen.getByText('Baseline').closest('span')).toHaveAttribute('title', 'ยังไม่มีข้อมูล Baseline (Sprint 14)')
  })
})
