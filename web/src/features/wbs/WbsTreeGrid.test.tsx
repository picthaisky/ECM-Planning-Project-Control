import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WbsTreeGrid } from './WbsTreeGrid'
import type { FlatWbsRow } from './flattenTree'

// DataTable's virtualizer needs a non-zero viewport under jsdom (see components/DataTable.test.tsx).
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

function makeRow(overrides: Partial<FlatWbsRow['node']> & { id: string }, extra?: Partial<FlatWbsRow>): FlatWbsRow {
  return {
    node: {
      parentWbsNodeId: null,
      code: overrides.id,
      title: overrides.id,
      weightPercentage: '10.00',
      activityCount: 0,
      children: [],
      ...overrides,
    },
    depth: 0,
    hasChildren: false,
    isExpanded: false,
    ...extra,
  }
}

describe('WbsTreeGrid', () => {
  it('renders WBS node columns (code, title, weight, activity count)', () => {
    const rows = [makeRow({ id: 'n1', code: '1', title: 'งานโครงสร้าง', activityCount: 12 })]
    render(<WbsTreeGrid rows={rows} state="ready" onToggle={vi.fn()} />)

    expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('10.00%')).toBeInTheDocument()
  })

  it('clicking a row with children calls onToggle with its id', async () => {
    const onToggle = vi.fn()
    const user = userEvent.setup()
    const rows = [makeRow({ id: 'n1', code: '1', title: 'มีลูก' }, { hasChildren: true })]
    render(<WbsTreeGrid rows={rows} state="ready" onToggle={onToggle} />)

    await user.click(screen.getByRole('button', { name: /มีลูก/ }))
    expect(onToggle).toHaveBeenCalledWith('n1')
  })

  it('shows a "+ เพิ่มกิจกรรม" action only for leaf nodes with activities, when onPickForProgress is supplied', () => {
    const rows = [
      makeRow({ id: 'leaf', code: 'L', title: 'มีกิจกรรม', activityCount: 3 }),
      makeRow({ id: 'empty', code: 'E', title: 'ไม่มีกิจกรรม', activityCount: 0 }),
    ]
    render(<WbsTreeGrid rows={rows} state="ready" onToggle={vi.fn()} onPickForProgress={vi.fn()} />)

    expect(screen.getAllByText('+ เพิ่มกิจกรรม')).toHaveLength(1)
  })

  it('shows the DataTable error state with the given message', () => {
    render(<WbsTreeGrid rows={[]} state="error" errorMessage="โหลดข้อมูล WBS ไม่สำเร็จ" onToggle={vi.fn()} />)
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดข้อมูล WBS ไม่สำเร็จ')
  })

  it('performance (ADR-0004, S4-FE-03 DoD "500+ แถวใช้ virtualization"): 600 WBS rows do not produce 600 DOM row nodes', () => {
    // Reuses the exact DOM-node-count assertion pattern DataTable.test.tsx established at the
    // primitive level (`[role="row"][data-index]`) - here applied one layer up, at the actual
    // screen-facing component, to confirm (not assume) that WbsTreeGrid's own column/action wiring
    // does not accidentally defeat DataTable's virtualization (e.g. by rendering an unvirtualized
    // wrapper around it, or passing every row through a path that forces full realization).
    const rows = Array.from({ length: 600 }, (_, i) =>
      makeRow({ id: `n${i}`, code: `${i}`, title: `WBS Node ${i}`, activityCount: i % 3 }),
    )
    const { container } = render(<WbsTreeGrid rows={rows} state="ready" onToggle={vi.fn()} />)

    const renderedBodyRows = container.querySelectorAll('[role="row"][data-index]')

    expect(rows.length).toBe(600)
    expect(renderedBodyRows.length).toBeGreaterThan(0)
    expect(renderedBodyRows.length).toBeLessThan(60)
    expect(renderedBodyRows.length).toBeLessThan(rows.length)
  })
})
