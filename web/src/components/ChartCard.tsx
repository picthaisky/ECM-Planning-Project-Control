import type { ReactNode } from 'react'
import { cx } from '../utils/cx'

export type ChartCardState = 'ready' | 'loading' | 'empty' | 'error'

export interface ChartCardProps {
  title: string
  subtitle?: ReactNode
  /** Rendered next to the title, e.g. a PV/EV/AC/EAC color legend. */
  legend?: ReactNode
  /** Rendered next to the legend, e.g. period selector / export buttons. */
  actions?: ReactNode
  state?: ChartCardState
  emptyMessage?: string
  errorMessage?: string
  className?: string
  /** The chart itself (SVG/canvas). Only rendered when `state === 'ready'`. */
  children: ReactNode
}

/** Card wrapper for chart content (S-Curve, Cash Flow, EVM), consistent white/border styling. */
export function ChartCard({
  title,
  subtitle,
  legend,
  actions,
  state = 'ready',
  emptyMessage = 'ไม่มีข้อมูลสำหรับช่วงเวลานี้',
  errorMessage = 'โหลดข้อมูลกราฟไม่สำเร็จ',
  className,
  children,
}: ChartCardProps) {
  return (
    <div
      className={cx('rounded-card border border-border bg-surface p-4', className)}
      data-state={state}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-heading text-sm font-semibold text-navy">{title}</h3>
          {subtitle && <p className="mt-0.5 text-xs text-text-faint">{subtitle}</p>}
        </div>
        {(legend || actions) && (
          <div className="flex items-center gap-3">
            {legend}
            {actions}
          </div>
        )}
      </div>

      <div className="mt-4">
        {state === 'loading' && (
          <div
            role="status"
            aria-label={`${title} กำลังโหลด`}
            className="flex h-56 w-full animate-pulse items-center justify-center rounded bg-bg text-xs text-text-faint"
          >
            กำลังโหลดกราฟ...
          </div>
        )}
        {state === 'empty' && (
          <div className="flex h-56 w-full items-center justify-center rounded border border-dashed border-border text-xs text-text-faint">
            {emptyMessage}
          </div>
        )}
        {state === 'error' && (
          <div
            role="alert"
            className="flex h-56 w-full items-center justify-center rounded bg-danger/5 text-xs text-danger"
          >
            {errorMessage}
          </div>
        )}
        {state === 'ready' && children}
      </div>
    </div>
  )
}
