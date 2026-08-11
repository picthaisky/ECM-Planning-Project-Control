import { Button, DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import { formatIssueCode, formatIssueDate, formatIssueDateTime, ISSUE_STATUS_LABELS, nextIssueActionLabel } from '../issueLabels'
import type { IssueLogDto } from '../types'

export interface IssueTableProps {
  items: IssueLogDto[]
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
  /** Role-gated by the caller (mirrors `ProjectIssuesController.WriteRoles`) — when false, no
   * advance-status action renders regardless of the issue's own status. */
  canWrite: boolean
  /** The issue id currently mid-`advance-status`, if any — disables just that row's button. */
  advancingId: string | null
  onAdvance: (issue: IssueLogDto) => void
}

/**
 * The issue/action register (S11-FE-01, prototype screen `docs/ECM Planning Prototype.dc.html`
 * ~line 502 "ปัญหาหน้างาน / Action Log") on the virtualized `DataTable` primitive (ADR-0004).
 * Column set and the inline "เริ่มแก้ไข →" / "ปิดปัญหา ✓" / "ปิดเมื่อ {date}" action reproduce the
 * prototype's own `issueRows`/`nextLabel`/`canNext`/`isClosed` logic (`issueLabels.ts`'s
 * `nextIssueActionLabel`) exactly — one rung per click (domain-rules.md §9.1: skipping `Doing` is
 * not permitted), never an "Open -> Closed" shortcut.
 */
export function IssueTable({ items, state, errorMessage, canWrite, advancingId, onAdvance }: IssueTableProps) {
  const columns: DataTableColumn<IssueLogDto>[] = [
    {
      key: 'no',
      header: 'เลขที่',
      width: 84,
      render: (row) => <span className="text-text-faint">{formatIssueCode(row.sequenceNo)}</span>,
    },
    {
      key: 'title',
      header: 'ปัญหา / รายละเอียด',
      render: (row) => (
        <div className="truncate">
          <div className="font-medium text-text">{row.title}</div>
          {row.detail && <div className="truncate text-[10.5px] text-text-faint">{row.detail}</div>}
        </div>
      ),
    },
    {
      key: 'owner',
      header: 'ผู้รับผิดชอบ',
      width: 140,
      render: (row) => <span className="text-text-muted">{row.owner ?? '—'}</span>,
    },
    {
      key: 'due',
      header: 'กำหนดแก้ไข',
      width: 110,
      render: (row) => <span className="text-text-muted">{row.dueDate ? formatIssueDate(row.dueDate) : '—'}</span>,
    },
    {
      key: 'status',
      header: 'สถานะ',
      width: 108,
      align: 'right',
      render: (row) => <StatusPill label={ISSUE_STATUS_LABELS[row.status]} status={row.status} />,
    },
    {
      key: 'actions',
      header: 'การดำเนินการ',
      width: 150,
      align: 'right',
      render: (row) => {
        if (row.status === 'Closed') {
          return <span className="text-[10.5px] text-text-faint">ปิดเมื่อ {row.closedAt ? formatIssueDateTime(row.closedAt) : '—'}</span>
        }
        if (!canWrite) return null
        const label = nextIssueActionLabel(row.status)
        if (!label) return null
        return (
          <Button size="sm" variant="secondary" loading={advancingId === row.id} onClick={() => onAdvance(row)}>
            {label}
          </Button>
        )
      },
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={items}
      getRowId={(row) => row.id}
      state={state}
      errorMessage={errorMessage}
      emptyMessage="ไม่มีรายการปัญหาที่ตรงกับเงื่อนไข"
      rowHeight={52}
      height={440}
      ariaLabel="ปัญหาหน้างาน / Action Log"
    />
  )
}
