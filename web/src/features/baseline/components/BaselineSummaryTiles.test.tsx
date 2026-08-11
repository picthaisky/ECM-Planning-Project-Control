import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { BaselineSummaryTiles } from './BaselineSummaryTiles'
import type { BaselineComparisonDto } from '../types'

const comparison: BaselineComparisonDto = {
  projectId: 'project-1',
  baselineId: 'baseline-1',
  baselineName: 'Baseline 1 - อนุมัติสัญญา',
  baselineCapturedAt: '2026-08-01T09:00:00+07:00',
  totalActivityCount: 250,
  driftedActivityCount: 12,
  projectFinishVarianceDays: 5,
  currentBac: '1050000.00',
  baselineBac: '1000000.00',
  bacVarianceAmount: '50000.00',
  activities: [],
}

describe('BaselineSummaryTiles', () => {
  it('shows exactly 3 tiles — never a 4th "critical path changed" tile (not reconstructable from the backend contract)', () => {
    render(<BaselineSummaryTiles comparison={comparison} state="ready" />)
    const grid = screen.getByTestId('baseline-summary-tiles')
    expect(grid.children).toHaveLength(3)
    expect(screen.queryByText(/Critical Path/)).not.toBeInTheDocument()
  })

  it('shows the project finish variance, drifted count, and BAC variance with the real numbers', () => {
    render(<BaselineSummaryTiles comparison={comparison} state="ready" />)
    expect(screen.getByText('+5 วัน')).toBeInTheDocument()
    expect(screen.getByText('12 / 250')).toBeInTheDocument()
    expect(screen.getByText('+50,000.00 บาท')).toBeInTheDocument()
    expect(screen.getByText(/Baseline 1 - อนุมัติสัญญา/)).toBeInTheDocument()
  })

  it('renders "—" (never fabricated) when projectFinishVarianceDays is null', () => {
    render(<BaselineSummaryTiles comparison={{ ...comparison, projectFinishVarianceDays: null }} state="ready" />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('shows the loading state with no comparison yet', () => {
    render(<BaselineSummaryTiles comparison={null} state="loading" />)
    expect(screen.getAllByRole('status')).toHaveLength(3)
  })
})
