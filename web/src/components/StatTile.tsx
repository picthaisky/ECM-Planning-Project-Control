import type { ReactNode } from 'react'
import { cx } from '../utils/cx'

export type StatTileTone = 'neutral' | 'success' | 'danger' | 'warning' | 'gold'
export type StatTileState = 'ready' | 'loading' | 'error'

export interface StatTileProps {
  label: string
  /** Pre-formatted value (money/percent formatting is the caller's responsibility per convention). */
  value?: ReactNode
  /** Sub-line, e.g. a formula caption like "EAC = BAC / CPI (MB)" or a delta note. */
  caption?: ReactNode
  /** Colors the value to match its meaning (e.g. danger for over-budget/behind-schedule). */
  tone?: StatTileTone
  state?: StatTileState
  errorMessage?: string
  className?: string
}

const toneClasses: Record<StatTileTone, string> = {
  neutral: 'text-navy',
  success: 'text-success',
  danger: 'text-danger',
  warning: 'text-warning-text',
  // EVM S-Curve's EV tile (S7-FE-01) — matches the prototype's EV line/value colour exactly
  // (`docs/ECM Planning Prototype.dc.html`'s `evmMetrics` mock, `/cmplus-ui`'s "EV line" token
  // note) — brand gold, not a one-off hex (`text-gold`, generated from `src/config/theme.ts`).
  gold: 'text-gold',
}

/** Dashboard KPI tile: label + value + optional formula/caption sub-line. */
export function StatTile({
  label,
  value,
  caption,
  tone = 'neutral',
  state = 'ready',
  errorMessage = 'โหลดข้อมูลไม่สำเร็จ',
  className,
}: StatTileProps) {
  const isEmpty = state === 'ready' && (value === undefined || value === null || value === '')

  return (
    <div
      className={cx('rounded-card border border-border bg-surface p-4', className)}
      data-state={state}
    >
      <p className="text-[11px] font-medium uppercase tracking-wide text-text-faint">{label}</p>

      {state === 'loading' && (
        <div className="mt-2 space-y-2" role="status" aria-label={`${label} กำลังโหลด`}>
          <div className="h-6 w-20 animate-pulse rounded bg-border" />
          <div className="h-3 w-28 animate-pulse rounded bg-border-subtle" />
        </div>
      )}

      {state === 'error' && (
        <div className="mt-2">
          <p className="font-heading text-2xl font-bold text-danger">—</p>
          <p className="mt-1 text-[10px] text-danger">{errorMessage}</p>
        </div>
      )}

      {state === 'ready' && (
        <div className="mt-2">
          <p
            className={cx(
              'font-heading text-2xl font-bold',
              isEmpty ? 'text-text-faint' : toneClasses[tone],
            )}
          >
            {isEmpty ? '—' : value}
          </p>
          {caption && <p className="mt-1 text-[10.5px] text-text-faint">{caption}</p>}
        </div>
      )}
    </div>
  )
}
