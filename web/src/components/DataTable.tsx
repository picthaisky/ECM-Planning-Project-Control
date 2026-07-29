import { memo, useRef } from 'react'
import type { ReactNode } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { cx } from '../utils/cx'

export type DataTableColumnAlign = 'left' | 'right' | 'center'

export interface DataTableColumn<T> {
  key: string
  header: ReactNode
  /** Fixed pixel width. Omit to distribute the column with `minmax(0, 1fr)`. */
  width?: number
  align?: DataTableColumnAlign
  render: (row: T, rowIndex: number) => ReactNode
}

export type DataTableState = 'ready' | 'loading' | 'error'

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[]
  rows: T[]
  getRowId: (row: T, index: number) => string | number
  state?: DataTableState
  errorMessage?: string
  emptyMessage?: string
  onRowClick?: (row: T) => void
  /** Row height in px — the virtualizer's size estimate (ADR-0004). */
  rowHeight?: number
  /** Scrollable viewport height in px. */
  height?: number
  /** Extra rows rendered above/below the visible window (ADR-0004). */
  overscan?: number
  className?: string
  ariaLabel?: string
}

const alignClass: Record<DataTableColumnAlign, string> = {
  left: 'text-left justify-start',
  right: 'text-right justify-end',
  center: 'text-center justify-center',
}

interface DataTableRowProps<T> {
  row: T
  rowIndex: number
  columns: DataTableColumn<T>[]
  gridTemplateColumns: string
  size: number
  start: number
  onRowClick?: (row: T) => void
}

function DataTableRowImpl<T>({
  row,
  rowIndex,
  columns,
  gridTemplateColumns,
  size,
  start,
  onRowClick,
}: DataTableRowProps<T>) {
  return (
    <div
      role="row"
      data-index={rowIndex}
      onClick={onRowClick ? () => onRowClick(row) : undefined}
      className={cx(
        'grid border-t border-border-subtle text-sm text-text-muted',
        onRowClick && 'cursor-pointer hover:bg-bg',
      )}
      style={{
        gridTemplateColumns,
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: size,
        transform: `translateY(${start}px)`,
      }}
    >
      {columns.map((col) => (
        <div
          key={col.key}
          role="gridcell"
          className={cx('flex items-center truncate px-3 py-2', col.align && alignClass[col.align])}
        >
          {col.render(row, rowIndex)}
        </div>
      ))}
    </div>
  )
}

// Cast keeps the component generic (`<T>`) after `memo` — memo's own typings
// would otherwise collapse the generic to `unknown` for callers (ADR-0004:
// "row props stable/memoized").
const DataTableRow = memo(DataTableRowImpl) as typeof DataTableRowImpl

/**
 * Virtualized data table (ADR-0004) for the WBS tree, milestone/payment lists,
 * VO/Issue/Weather logs, etc. Only rows inside the visible window + overscan
 * buffer are mounted, regardless of how many `rows` are passed in.
 */
export function DataTable<T>({
  columns,
  rows,
  getRowId,
  state = 'ready',
  errorMessage = 'โหลดข้อมูลไม่สำเร็จ',
  emptyMessage = 'ไม่มีข้อมูล',
  onRowClick,
  rowHeight = 40,
  height = 420,
  overscan = 8,
  className,
  ariaLabel = 'data table',
}: DataTableProps<T>) {
  const scrollRef = useRef<HTMLDivElement>(null)

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    overscan,
  })

  const gridTemplateColumns = columns
    .map((col) => (col.width ? `${col.width}px` : 'minmax(0, 1fr)'))
    .join(' ')

  return (
    <div
      className={cx('overflow-hidden rounded-card border border-border bg-surface', className)}
      data-state={state}
      role="table"
      aria-label={ariaLabel}
      aria-rowcount={rows.length}
    >
      <div
        role="row"
        className="grid border-b border-border bg-surface-muted text-[10.5px] font-semibold uppercase tracking-wide text-text-faint"
        style={{ gridTemplateColumns }}
      >
        {columns.map((col) => (
          <div
            key={col.key}
            role="columnheader"
            className={cx('truncate px-3 py-2', col.align ? alignClass[col.align] : 'text-left')}
          >
            {col.header}
          </div>
        ))}
      </div>

      {state === 'loading' ? (
        <div
          role="status"
          className="flex items-center justify-center py-10 text-xs text-text-faint"
        >
          กำลังโหลดข้อมูล...
        </div>
      ) : state === 'error' ? (
        <div role="alert" className="flex items-center justify-center py-10 text-xs text-danger">
          {errorMessage}
        </div>
      ) : rows.length === 0 ? (
        <div className="flex items-center justify-center py-10 text-xs text-text-faint">
          {emptyMessage}
        </div>
      ) : (
        <div ref={scrollRef} className="overflow-auto" style={{ height }}>
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
            {virtualizer.getVirtualItems().map((virtualRow) => (
              <DataTableRow
                key={getRowId(rows[virtualRow.index], virtualRow.index)}
                row={rows[virtualRow.index]}
                rowIndex={virtualRow.index}
                columns={columns}
                gridTemplateColumns={gridTemplateColumns}
                size={virtualRow.size}
                start={virtualRow.start}
                onRowClick={onRowClick}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
