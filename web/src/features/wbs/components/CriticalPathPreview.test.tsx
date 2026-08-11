import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CriticalPathPreview } from './CriticalPathPreview'
import type { useRecalculateCpm } from '../useRecalculateCpm'
import type { RecalculateCpmResult } from '../types'

/**
 * S5-FE-01 component-level coverage — same "hand-built hook-shaped prop" pattern as
 * `ProgressUpdatePanel.test.tsx`'s `makeForm`, so this proves the wiring between
 * `useRecalculateCpm`'s return shape and this component directly, independent of the hook's own
 * tests (`useRecalculateCpm.test.ts`).
 */

type Cpm = ReturnType<typeof useRecalculateCpm>

function makeCpm(overrides: Partial<Cpm> = {}): Cpm {
  return {
    state: 'idle',
    error: null,
    result: null,
    recalculate: vi.fn().mockResolvedValue(null),
    ...overrides,
  }
}

const sampleResult: RecalculateCpmResult = {
  activitiesProcessed: 4,
  criticalActivityCount: 3,
  projectDurationDays: 15,
  criticalPath: ['activity-a', 'activity-c', 'activity-d'],
}

describe('CriticalPathPreview', () => {
  it('renders the "คำนวณ CPM ใหม่" button in idle state with no status pill', () => {
    render(<CriticalPathPreview cpm={makeCpm()} />)
    expect(screen.getByRole('button', { name: 'คำนวณ CPM ใหม่' })).toBeInTheDocument()
    expect(screen.queryByText('คำนวณสำเร็จ')).not.toBeInTheDocument()
    expect(screen.queryByText('คำนวณไม่สำเร็จ')).not.toBeInTheDocument()
  })

  it('shows a calculating status while the request is in flight', () => {
    render(<CriticalPathPreview cpm={makeCpm({ state: 'calculating' })} />)
    expect(screen.getByRole('button', { name: 'คำนวณ CPM ใหม่' })).toBeDisabled()
    expect(screen.getByText(/กำลังคำนวณเส้นทางวิกฤต/)).toBeInTheDocument()
  })

  it('clicking the button calls cpm.recalculate', async () => {
    const user = userEvent.setup()
    const cpm = makeCpm()
    render(<CriticalPathPreview cpm={cpm} />)

    await user.click(screen.getByRole('button', { name: 'คำนวณ CPM ใหม่' }))

    expect(cpm.recalculate).toHaveBeenCalledTimes(1)
  })

  it('on success, shows the success pill, the summary stats, and the critical path in schedule order (not re-sorted)', () => {
    render(<CriticalPathPreview cpm={makeCpm({ state: 'success', result: sampleResult })} />)

    expect(screen.getByText('คำนวณสำเร็จ')).toBeInTheDocument()

    const listItems = screen.getAllByRole('listitem')
    expect(listItems).toHaveLength(3)
    // Exact schedule order from the backend, with 1-based sequence numbers — never alphabetically
    // or otherwise re-sorted client-side.
    expect(listItems[0]).toHaveTextContent('1')
    expect(listItems[0]).toHaveTextContent('activity-a')
    expect(listItems[1]).toHaveTextContent('2')
    expect(listItems[1]).toHaveTextContent('activity-c')
    expect(listItems[2]).toHaveTextContent('3')
    expect(listItems[2]).toHaveTextContent('activity-d')

    expect(screen.getByText('4')).toBeInTheDocument() // activitiesProcessed
    expect(screen.getByText('15')).toBeInTheDocument() // projectDurationDays
  })

  it('shows an honest empty state when the recalculation finds zero critical activities', () => {
    render(
      <CriticalPathPreview
        cpm={makeCpm({ state: 'success', result: { ...sampleResult, criticalActivityCount: 0, criticalPath: [] } })}
      />,
    )

    expect(screen.getByText(/ไม่พบกิจกรรมวิกฤต/)).toBeInTheDocument()
    expect(screen.queryAllByRole('listitem')).toHaveLength(0)
  })

  it('on failure, shows the danger status pill and the Thai error message (e.g. a detected cycle)', () => {
    render(
      <CriticalPathPreview
        cpm={makeCpm({
          state: 'error',
          error: 'พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle) ไม่สามารถคำนวณตารางเวลาได้',
        })}
      />,
    )

    expect(screen.getByText('คำนวณไม่สำเร็จ')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('วนซ้ำ')
  })
})
