import { DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import {
  formatHours,
  formatManpowerDate,
  formatManpowerDateTime,
  formatWorkerCount,
  LABOUR_TYPE_LABELS,
  MANPOWER_ENTRY_KIND_LABELS,
  SHIFT_LABELS,
} from '../maneqLabels'
import type { ManpowerLogDto } from '../types'

export interface ManpowerLogTableProps {
  rows: ManpowerLogDto[]
  canWrite: boolean
  onRequestCorrection: (row: ManpowerLogDto) => void
}

/**
 * S12-FE-02's "บันทึกกำลังคน/เครื่องจักรรายวัน" table (prototype ~line 546) — **session-local only**.
 * The Sprint 12 backend exposes no `GET` list endpoint for `ManpowerEquipmentLog` (confirmed directly
 * against `ProjectManpowerLogsController.cs` — only the two `POST` writes and the
 * `productivity-index` read exist), so this table shows exactly what this browser session has itself
 * recorded (`ManeqPage.tsx`'s own accumulator, seeded from each write's real response), newest first.
 * This is real, live data — just not a full historical register, and the page says so
 * (`ManeqPage.tsx`'s own banner) rather than implying otherwise.
 */
export function ManpowerLogTable({ rows, canWrite, onRequestCorrection }: ManpowerLogTableProps) {
  const columns: DataTableColumn<ManpowerLogDto>[] = [
    { key: 'date', header: 'วันที่', width: 110, render: (row) => formatManpowerDate(row.logDate) },
    { key: 'shift', header: 'กะ', width: 100, render: (row) => SHIFT_LABELS[row.shift] },
    {
      key: 'workCategory',
      header: 'หมวดงาน',
      render: (row) => <span className="truncate font-mono text-[10.5px]" title={row.workCategoryId}>{row.workCategoryId}</span>,
    },
    { key: 'labourType', header: 'ประเภทแรงงาน', width: 130, render: (row) => LABOUR_TYPE_LABELS[row.labourType] },
    { key: 'workerCount', header: 'คน', width: 70, align: 'right', render: (row) => formatWorkerCount(row.workerCount) },
    { key: 'manHours', header: 'ชม.แรงงาน', width: 90, align: 'right', render: (row) => `${formatHours(row.manHours)}${row.manHoursDerived ? ' ≈' : ''}` },
    { key: 'equipmentCount', header: 'เครื่องจักร', width: 90, align: 'right', render: (row) => row.equipmentCount },
    {
      key: 'entryKind',
      header: 'ประเภทรายการ',
      width: 110,
      render: (row) => (
        <StatusPill
          label={MANPOWER_ENTRY_KIND_LABELS[row.entryKind]}
          tone={row.entryKind === 'Original' ? 'neutral' : row.entryKind === 'Retraction' ? 'danger' : 'warning'}
        />
      ),
    },
    { key: 'recordedAt', header: 'บันทึกเมื่อ', width: 150, render: (row) => formatManpowerDateTime(row.recordedAt) },
    ...(canWrite
      ? [
          {
            key: 'actions',
            header: '',
            width: 80,
            render: (row: ManpowerLogDto) =>
              row.entryKind !== 'Retraction' ? (
                <button
                  type="button"
                  onClick={() => onRequestCorrection(row)}
                  className="text-[10.5px] font-medium text-navy underline decoration-dotted hover:text-navy/70"
                >
                  แก้ไข
                </button>
              ) : null,
          } satisfies DataTableColumn<ManpowerLogDto>,
        ]
      : []),
  ]

  return (
    <DataTable
      columns={columns}
      rows={rows}
      getRowId={(row) => row.id}
      emptyMessage="ยังไม่มีบันทึกในเซสชันนี้ — กด '+ บันทึกวันนี้' เพื่อเริ่มบันทึก"
      ariaLabel="ตารางบันทึกกำลังคน/เครื่องจักร"
      height={320}
    />
  )
}
