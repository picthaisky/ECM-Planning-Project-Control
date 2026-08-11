import { DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import { cx } from '../../../utils/cx'
import { formatMoney } from '../../../utils/format'
import { VO_STATUS_LABELS, VO_TYPE_LABELS } from '../voStatusLabels'
import type { VariationOrderDto } from '../types'

export interface VoTableProps {
  variationOrders: VariationOrderDto[]
  selectedId: string | null
  onSelect: (id: string) => void
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
}

/**
 * S10-FE-01: the VO register (prototype screen #9's table, `docs/ECM Planning Prototype.dc.html`
 * ~line 390: "รายการ Variation Order"), rebuilt on the virtualized `DataTable` primitive (ADR-0004)
 * instead of the prototype's plain static grid — same discipline as
 * `features/payment/components/MilestoneCertificateTable.tsx`. Columns mirror the prototype's
 * เลขที่/รายการ/ประเภท/มูลค่า/สถานะ set; "การดำเนินการ" (the prototype's inline อนุมัติ/ตีกลับ buttons)
 * is deliberately **not** reproduced as an inline row action — the real approval chain needs a
 * mandatory-comment confirmation flow (`ApprovalChainBar`/`ApprovalActionModal`), so acting on a VO
 * happens in the detail panel after selecting its row, exactly like `MilestoneCertificateTable`
 * already established for Payment Certificate.
 */
export function VoTable({ variationOrders, selectedId, onSelect, state, errorMessage }: VoTableProps) {
  const columns: DataTableColumn<VariationOrderDto>[] = [
    {
      key: 'no',
      header: 'เลขที่',
      width: 92,
      render: (row) => (
        <span className={cx('font-semibold', row.id === selectedId ? 'text-gold' : 'text-text-faint')}>
          {row.voNumber}
        </span>
      ),
    },
    { key: 'title', header: 'รายการ', render: (row) => row.description ?? '—' },
    {
      key: 'type',
      header: 'ประเภท',
      width: 88,
      render: (row) => (
        <span
          className={cx(
            'rounded px-2 py-0.5 text-[10.5px] font-semibold',
            row.type === 'Add' ? 'bg-secondary/10 text-secondary' : 'bg-danger/10 text-danger',
          )}
        >
          {VO_TYPE_LABELS[row.type]}
        </span>
      ),
    },
    {
      key: 'amount',
      header: 'มูลค่า (บาท)',
      width: 148,
      align: 'right',
      render: (row) => `${Number(row.amount) > 0 ? '+' : ''}${formatMoney(row.amount)}`,
    },
    {
      key: 'status',
      header: 'สถานะ',
      width: 108,
      align: 'right',
      render: (row) => <StatusPill label={VO_STATUS_LABELS[row.status]} status={row.status} />,
    },
  ]

  return (
    <div className="overflow-hidden rounded-card border border-border bg-surface">
      <div className="px-4 py-3 font-heading text-[13.5px] font-semibold text-navy">
        รายการ Variation Order — คลิกเพื่อดูรายละเอียด
      </div>
      <DataTable
        columns={columns}
        rows={variationOrders}
        getRowId={(row) => row.id}
        state={state}
        errorMessage={errorMessage}
        emptyMessage="ยังไม่มี Variation Order ในโครงการนี้"
        onRowClick={(row) => onSelect(row.id)}
        rowHeight={44}
        height={420}
        ariaLabel="รายการ Variation Order"
        className="rounded-none border-0 border-t border-border"
      />
    </div>
  )
}
