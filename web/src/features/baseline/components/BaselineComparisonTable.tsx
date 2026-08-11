import { DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import { formatMoney } from '../../../utils/format'
import type { BaselineActivityDeltaDto } from '../types'

export interface BaselineComparisonTableProps {
  activities: BaselineActivityDeltaDto[]
  state: 'ready' | 'loading' | 'error'
  errorMessage?: string
}

const dateFormatter = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeZone: 'Asia/Bangkok' })

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : dateFormatter.format(parsed)
}

function signedDays(days: number | null): string {
  if (days === null) return '—'
  if (days === 0) return '0 วัน'
  return days > 0 ? `+${days} วัน (ช้า)` : `${days} วัน (เร็ว)`
}

function signedMoney(amount: string | null): string {
  if (amount === null) return '—'
  const numeric = Number(amount)
  const sign = Number.isFinite(numeric) && numeric > 0 ? '+' : ''
  return `${sign}${formatMoney(amount)} บาท`
}

function varianceTone(days: number | null): string {
  if (days === null) return 'text-text-faint'
  if (days > 0) return 'text-danger'
  if (days < 0) return 'text-success'
  return 'text-text-muted'
}

function budgetVarianceTone(amount: string | null): string {
  if (amount === null) return 'text-text-faint'
  const numeric = Number(amount)
  if (!Number.isFinite(numeric) || numeric === 0) return 'text-text-muted'
  return numeric > 0 ? 'text-danger' : 'text-success'
}

/**
 * S14-FE-01: the delta table — "ปัจจุบัน − Baseline" per activity (S14-BE-02's own contract), on
 * the **virtualized** `DataTable` primitive (ADR-0004) per this ticket's explicit instruction to
 * reuse Sprint 6's approach rather than inventing a second virtualization mechanism. Only rows
 * inside the visible window + overscan buffer are ever mounted, so this stays smooth at the same
 * 10,000-activity scale `DataTable`'s other consumers (WBS/Payment/VO/Weather logs) already rely
 * on it for.
 *
 * The `Critical` column shows the activity's **current** criticality only (`isCritical`) — never a
 * "critical path changed since baseline" claim, which is not reconstructable from what
 * `BaselineActivitySnapshot` persists (see `BaselineSummaryTiles.tsx`'s own remarks on the omitted
 * 4th prototype tile for the full reasoning).
 */
export function BaselineComparisonTable({ activities, state, errorMessage }: BaselineComparisonTableProps) {
  const columns: DataTableColumn<BaselineActivityDeltaDto>[] = [
    {
      key: 'code',
      header: 'รหัส',
      width: 96,
      render: (row) => <span className="font-medium text-text">{row.activityCode ?? '—'}</span>,
    },
    {
      key: 'name',
      header: 'ชื่อกิจกรรม',
      render: (row) => (
        <div className="truncate">
          <div className="truncate text-text-muted">{row.name ?? '—'}</div>
          {row.isRemoved && (
            <StatusPill label="ไม่มีในกำหนดการปัจจุบันแล้ว" tone="warning" className="mt-0.5" />
          )}
        </div>
      ),
    },
    {
      key: 'baselineDates',
      header: 'Baseline (เริ่ม → สิ้นสุด)',
      width: 190,
      render: (row) => (
        <span className="text-text-muted">
          {formatDate(row.baselinePlannedStart)} → {formatDate(row.baselinePlannedFinish)}
        </span>
      ),
    },
    {
      key: 'currentDates',
      header: 'ปัจจุบัน (เริ่ม → สิ้นสุด)',
      width: 190,
      render: (row) =>
        row.isRemoved ? (
          <span className="text-text-faint">ถูกลบออกจากกำหนดการ</span>
        ) : (
          <span className="text-text-muted">
            {formatDate(row.currentPlannedStart)} → {formatDate(row.currentPlannedFinish)}
          </span>
        ),
    },
    {
      key: 'finishVariance',
      header: 'ผลต่างวันสิ้นสุด',
      width: 130,
      align: 'right',
      render: (row) => <span className={varianceTone(row.finishVarianceDays)}>{signedDays(row.finishVarianceDays)}</span>,
    },
    {
      key: 'baselineBudget',
      header: 'งบประมาณ Baseline',
      width: 140,
      align: 'right',
      render: (row) => <span className="text-text-muted">{formatMoney(row.baselineBudgetCost)} บาท</span>,
    },
    {
      key: 'currentBudget',
      header: 'งบประมาณปัจจุบัน',
      width: 140,
      align: 'right',
      render: (row) => <span className="text-text-muted">{row.currentBudgetCost === null ? '—' : `${formatMoney(row.currentBudgetCost)} บาท`}</span>,
    },
    {
      key: 'budgetVariance',
      header: 'ผลต่างงบประมาณ',
      width: 140,
      align: 'right',
      render: (row) => <span className={budgetVarianceTone(row.budgetVarianceAmount)}>{signedMoney(row.budgetVarianceAmount)}</span>,
    },
    {
      key: 'critical',
      header: 'Critical',
      width: 90,
      align: 'center',
      render: (row) =>
        row.isCritical === null ? (
          <span className="text-text-faint">—</span>
        ) : row.isCritical ? (
          <StatusPill label="Critical" tone="danger" />
        ) : (
          <StatusPill label="ปกติ" tone="neutral" />
        ),
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={activities}
      getRowId={(row) => row.activityId}
      state={state}
      errorMessage={errorMessage}
      emptyMessage="ไม่มีข้อมูลกิจกรรมสำหรับเปรียบเทียบ"
      rowHeight={56}
      height={480}
      ariaLabel="ตารางเปรียบเทียบแผนปัจจุบันกับ Baseline"
    />
  )
}
