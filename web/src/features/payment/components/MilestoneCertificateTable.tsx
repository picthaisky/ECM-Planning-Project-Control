import { DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import { cx } from '../../../utils/cx'
import { formatMoney, formatPercent } from '../../../utils/format'
import { PAYMENT_STATUS_LABELS } from '../paymentStatusLabels'
import type { PaymentCertificateDto } from '../types'

export interface MilestoneCertificateTableProps {
  certificates: PaymentCertificateDto[]
  selectedId: string | null
  onSelect: (id: string) => void
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
}

function percentOrDash(value: string | null): string {
  return value === null ? '—' : formatPercent(value)
}

/**
 * S9-FE-01: the milestone list (prototype screen #7's left column, "งวดงาน — คลิกเพื่อดู
 * Certificate", `docs/ECM Planning Prototype.dc.html` line ~314), rebuilt on the virtualized
 * `DataTable` primitive (ADR-0004) instead of the prototype's plain static grid — same discipline
 * as `features/info/ImportHistoryCard.tsx`. Columns mirror the prototype's งวด/รายละเอียด/มูลค่า/
 * ทำจริง%/ยื่นเบิก%/อนุมัติ%/สถานะ set exactly.
 */
export function MilestoneCertificateTable({
  certificates,
  selectedId,
  onSelect,
  state,
  errorMessage,
}: MilestoneCertificateTableProps) {
  const columns: DataTableColumn<PaymentCertificateDto>[] = [
    {
      key: 'no',
      header: 'งวด',
      width: 56,
      render: (row) => (
        <span className={cx('font-semibold', row.id === selectedId ? 'text-gold' : 'text-text-faint')}>
          {row.milestoneNo}
        </span>
      ),
    },
    { key: 'desc', header: 'รายละเอียด', render: (row) => row.description ?? '—' },
    {
      key: 'value',
      header: 'มูลค่า (บาท)',
      width: 132,
      align: 'right',
      render: (row) => formatMoney(row.milestoneValue),
    },
    { key: 'actual', header: 'ทำจริง%', width: 76, align: 'right', render: (row) => percentOrDash(row.actualProgressPct) },
    { key: 'claim', header: 'ยื่นเบิก%', width: 76, align: 'right', render: (row) => percentOrDash(row.claimPct) },
    { key: 'approve', header: 'อนุมัติ%', width: 76, align: 'right', render: (row) => formatPercent(row.approvePct) },
    {
      key: 'status',
      header: 'สถานะ',
      width: 108,
      align: 'right',
      render: (row) => <StatusPill label={PAYMENT_STATUS_LABELS[row.status]} status={row.status} />,
    },
  ]

  return (
    <div className="overflow-hidden rounded-card border border-border bg-surface">
      <div className="px-4 py-3 font-heading text-[13.5px] font-semibold text-navy">
        งวดงาน — คลิกเพื่อดู Certificate
      </div>
      <DataTable
        columns={columns}
        rows={certificates}
        getRowId={(row) => row.id}
        state={state}
        errorMessage={errorMessage}
        emptyMessage="ยังไม่มีใบรับรองผลงานในโครงการนี้"
        onRowClick={(row) => onSelect(row.id)}
        rowHeight={44}
        height={420}
        ariaLabel="รายการงวดงาน"
        className="rounded-none border-0 border-t border-border"
      />
    </div>
  )
}
