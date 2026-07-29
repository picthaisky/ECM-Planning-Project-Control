import { cx } from '../utils/cx'
import { resolveStatusTone } from './statusTone'

export type StatusPillTone = 'success' | 'danger' | 'warning' | 'neutral'

export interface StatusPillProps {
  label: string
  /** Explicit tone override. If omitted, derived from `status` via `resolveStatusTone`. */
  tone?: StatusPillTone
  /** Raw status keyword auto-mapped to a tone (e.g. "Open"/"Doing"/"Closed", "Approved"/"Rejected"/"Pending"). Ignored if `tone` is set. */
  status?: string
  className?: string
}

const toneClasses: Record<StatusPillTone, string> = {
  success: 'bg-success/10 text-success',
  danger: 'bg-danger/10 text-danger',
  warning: 'bg-warning-text/10 text-warning-text',
  neutral: 'bg-border text-text-muted',
}

/** Colored status badge (Open/Doing/Closed, Approved/Rejected/Pending, ...). */
export function StatusPill({ label, tone, status, className }: StatusPillProps) {
  const text = label && label.trim().length > 0 ? label : '—'
  const resolvedTone = tone ?? (status ? resolveStatusTone(status) : 'neutral')

  return (
    <span
      className={cx(
        'inline-flex items-center rounded-[5px] px-[9px] py-[3px] text-[10.5px] font-semibold leading-none',
        toneClasses[resolvedTone],
        className,
      )}
    >
      {text}
    </span>
  )
}
