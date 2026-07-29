import { DataTable } from '../../components'
import type { DataTableColumn, DataTableState } from '../../components'
import { cx } from '../../utils/cx'
import { formatPercent } from '../../utils/format'
import type { FlatWbsRow } from './flattenTree'

export interface WbsTreeGridProps {
  rows: FlatWbsRow[]
  state: DataTableState
  errorMessage?: string
  onToggle: (nodeId: string) => void
  /** When set, a leaf row (`activityCount > 0`, no children) shows a "+ เพิ่มกิจกรรม" action that
   * loads that node's activities into the batch-progress grid (`ProgressUpdatePanel`). `null`
   * outside update-progress mode. */
  onPickForProgress?: (row: FlatWbsRow) => void
}

/**
 * The read/browse half of the S4-FE-03 WBS & Activity screen: the real WBS tree
 * (`GET .../wbs-tree`, S4-BE-01), flattened (`flattenTree.ts`) and rendered on the virtualized
 * `DataTable` primitive (ADR-0004 — 500+ rows never render one DOM node per row). Columns match
 * the prototype's WBS screen (`docs/ECM Planning Prototype.dc.html`) at the granularity the real
 * API actually returns: **WBS nodes**, not individual activities (`ActivityCount` is a rollup, per
 * `GetWbsTreeQuery`'s own design) — the prototype's mock per-activity Duration/%/status columns
 * are not reproduced here since the live payload has no per-activity data to show them from.
 */
export function WbsTreeGrid({ rows, state, errorMessage, onToggle, onPickForProgress }: WbsTreeGridProps) {
  const columns: DataTableColumn<FlatWbsRow>[] = [
    {
      key: 'code',
      header: 'WBS Code',
      width: 110,
      render: (row) => <span className="text-text-faint">{row.node.code}</span>,
    },
    {
      key: 'title',
      header: 'ชื่องาน',
      render: (row) => (
        <button
          type="button"
          onClick={() => onToggle(row.node.id)}
          disabled={!row.hasChildren}
          style={{ paddingLeft: row.depth * 18 }}
          className={cx(
            'flex w-full items-center gap-1.5 truncate text-left',
            row.hasChildren ? 'cursor-pointer' : 'cursor-default',
            row.depth === 0 ? 'font-semibold text-text' : 'font-normal text-text',
          )}
        >
          <span aria-hidden="true" className="w-2.5 text-[9px] text-text-faint">
            {row.hasChildren ? (row.isExpanded ? '▼' : '►') : ''}
          </span>
          {row.node.title}
        </button>
      ),
    },
    {
      key: 'weight',
      header: 'Weight',
      width: 90,
      align: 'right',
      render: (row) => formatPercent(row.node.weightPercentage),
    },
    {
      key: 'activityCount',
      header: 'จำนวนกิจกรรม',
      width: 110,
      align: 'right',
      render: (row) => row.node.activityCount.toLocaleString('th-TH'),
    },
    ...(onPickForProgress
      ? [
          {
            key: 'action',
            header: '',
            width: 140,
            align: 'right' as const,
            render: (row: FlatWbsRow) =>
              row.node.activityCount > 0 ? (
                <button
                  type="button"
                  onClick={() => onPickForProgress(row)}
                  className="text-[11px] font-medium text-navy underline decoration-dotted"
                >
                  + เพิ่มกิจกรรม
                </button>
              ) : null,
          },
        ]
      : []),
  ]

  return (
    <DataTable
      columns={columns}
      rows={rows}
      getRowId={(row) => row.node.id}
      state={state}
      errorMessage={errorMessage}
      emptyMessage="ไม่พบข้อมูล WBS สำหรับโครงการนี้"
      rowHeight={40}
      height={520}
      ariaLabel="ตาราง WBS"
    />
  )
}
