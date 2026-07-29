import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DataTable } from './DataTable'
import type { DataTableColumn } from './DataTable'

interface Row {
  id: number
  code: string
  name: string
  progress: number
}

function buildRows(count: number): Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i,
    code: `WBS-${i + 1}`,
    name: `Activity ${i + 1}`,
    progress: (i % 100) / 100,
  }))
}

const columns: DataTableColumn<Row>[] = [
  { key: 'code', header: 'Code', width: 120, render: (row) => row.code },
  { key: 'name', header: 'Name', render: (row) => row.name },
  {
    key: 'progress',
    header: '%',
    width: 80,
    align: 'right',
    render: (row) => `${(row.progress * 100).toFixed(2)}%`,
  },
]

// jsdom performs no real layout, so the scroll container's offsetWidth/Height
// (what @tanstack/react-virtual reads to size the viewport) default to 0,
// which would make every test render zero rows. Stub a realistic viewport so
// the virtualizer computes a real, bounded visible window instead.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
    configurable: true,
    get() {
      return 420
    },
  })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
    configurable: true,
    get() {
      return 800
    },
  })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

describe('DataTable', () => {
  it('happy path: renders the header and visible rows', () => {
    render(<DataTable columns={columns} rows={buildRows(5)} getRowId={(row) => row.id} />)
    expect(screen.getByRole('columnheader', { name: 'Code' })).toBeInTheDocument()
    expect(screen.getByText('WBS-1')).toBeInTheDocument()
  })

  it('loading state: shows a loading indicator and no rows', () => {
    render(<DataTable columns={columns} rows={[]} getRowId={(row) => row.id} state="loading" />)
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByRole('gridcell')).not.toBeInTheDocument()
  })

  it('error state: shows the error message and no rows', () => {
    render(
      <DataTable
        columns={columns}
        rows={[]}
        getRowId={(row) => row.id}
        state="error"
        errorMessage="เชื่อมต่อไม่สำเร็จ"
      />,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('เชื่อมต่อไม่สำเร็จ')
    expect(screen.queryByRole('gridcell')).not.toBeInTheDocument()
  })

  it('empty state: shows the empty message when there are zero rows', () => {
    render(
      <DataTable
        columns={columns}
        rows={[]}
        getRowId={(row) => row.id}
        emptyMessage="ไม่พบข้อมูล WBS"
      />,
    )
    expect(screen.getByText('ไม่พบข้อมูล WBS')).toBeInTheDocument()
  })

  it('fires onRowClick with the clicked row', async () => {
    const onRowClick = vi.fn()
    const user = userEvent.setup()
    render(
      <DataTable
        columns={columns}
        rows={buildRows(5)}
        getRowId={(row) => row.id}
        onRowClick={onRowClick}
      />,
    )
    await user.click(screen.getByText('WBS-1'))
    expect(onRowClick).toHaveBeenCalledWith(expect.objectContaining({ code: 'WBS-1' }))
  })

  it('performance (ADR-0004): 1,000 rows do not produce 1,000 DOM row nodes — only the visible window + overscan', () => {
    const rows = buildRows(1000)
    const { container } = render(
      <DataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        rowHeight={40}
        height={420}
        overscan={8}
      />,
    )

    // Only body rows carry `data-index`; the header row does not, so this
    // selector counts exactly the virtualized row nodes actually in the DOM.
    const renderedBodyRows = container.querySelectorAll('[role="row"][data-index]')

    expect(rows.length).toBe(1000)
    expect(renderedBodyRows.length).toBeGreaterThan(0)
    // Viewport (420px / 40px row ≈ 10.5 visible) + 8 overscan above/below is
    // nowhere near 1,000 — assert a generous but real upper bound.
    expect(renderedBodyRows.length).toBeLessThan(60)
    expect(renderedBodyRows.length).toBeLessThan(rows.length)
  })
})
