import { useState } from 'react'
import { Button, DataTable, Modal, StatusPill } from '../../components'
import type { DataTableColumn } from '../../components'
import { parseImportErrorJson } from './errorTranslation'
import type { FileImportJob } from './types'
import type { HistoryState } from './useImportWizard'

const FORMAT_LABEL: Record<FileImportJob['format'], string> = {
  Xer: 'Primavera P6 (.XER)',
  Mspdi: 'MS Project (MSPDI)',
  Excel: 'Excel (ความคืบหน้า)',
}

const dateTimeFormatter = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

function formatStartedAt(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : dateTimeFormatter.format(parsed)
}

export interface ImportHistoryCardProps {
  jobs: FileImportJob[]
  state: HistoryState
  errorMessage: string | null
  onRetry: () => void
}

/**
 * History step (S3-FE-01) — "ประวัติการนำเข้าล่าสุด" card from the prototype, rebuilt on the
 * virtualized `DataTable` primitive (ADR-0004) per the DoD ("reuse it, don't rebuild a table")
 * instead of the prototype's plain static list.
 */
export function ImportHistoryCard({ jobs, state, errorMessage, onRetry }: ImportHistoryCardProps) {
  const [selectedJob, setSelectedJob] = useState<FileImportJob | null>(null)
  const selectedError = selectedJob ? parseImportErrorJson(selectedJob.errorJson) : null

  const columns: DataTableColumn<FileImportJob>[] = [
    {
      key: 'status',
      header: 'สถานะ',
      width: 88,
      render: (job) => <StatusPill label={job.status} status={job.status} />,
    },
    {
      key: 'file',
      header: 'ไฟล์',
      render: (job) => (
        <div className="min-w-0">
          <div className="truncate font-medium text-text">{job.fileName}</div>
          <div className="truncate text-[10.5px] text-text-faint">{FORMAT_LABEL[job.format]}</div>
        </div>
      ),
    },
    {
      key: 'rows',
      header: 'จำนวนแถว',
      width: 96,
      align: 'right',
      render: (job) =>
        job.status === 'Succeeded' ? job.rowsImported.toLocaleString('th-TH') : '—',
    },
    {
      key: 'startedAt',
      header: 'นำเข้าเมื่อ',
      width: 150,
      render: (job) => formatStartedAt(job.startedAt),
    },
    {
      key: 'actions',
      header: 'รายละเอียด',
      width: 100,
      align: 'right',
      render: (job) =>
        job.status === 'Failed' ? (
          <button
            type="button"
            onClick={() => setSelectedJob(job)}
            className="text-[11px] font-medium text-navy underline decoration-dotted"
          >
            ดูสาเหตุ
          </button>
        ) : (
          '—'
        ),
    },
  ]

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="mb-3 flex items-center">
        <div className="font-heading text-[13.5px] font-semibold text-navy">
          ประวัติการนำเข้าล่าสุด
        </div>
        <button
          type="button"
          onClick={onRetry}
          disabled={state === 'loading'}
          className="ml-auto text-[10.5px] font-medium text-navy underline decoration-dotted disabled:cursor-not-allowed disabled:opacity-50"
        >
          รีเฟรช
        </button>
      </div>

      <DataTable
        columns={columns}
        rows={jobs}
        getRowId={(job) => job.id}
        state={state === 'loading' ? 'loading' : state === 'error' ? 'error' : 'ready'}
        errorMessage={errorMessage ?? 'โหลดประวัติการนำเข้าไม่สำเร็จ'}
        emptyMessage="ยังไม่มีประวัติการนำเข้าไฟล์สำหรับโครงการนี้"
        rowHeight={52}
        height={280}
        ariaLabel="ประวัติการนำเข้าไฟล์"
      />

      <Modal
        isOpen={selectedJob !== null}
        onClose={() => setSelectedJob(null)}
        title="รายละเอียดข้อผิดพลาดการนำเข้า"
        footer={
          <Button variant="secondary" size="sm" onClick={() => setSelectedJob(null)}>
            ปิด
          </Button>
        }
      >
        {selectedJob && (
          <div className="space-y-2">
            <p className="text-xs text-text-faint">
              {selectedJob.fileName} · {formatStartedAt(selectedJob.startedAt)}
            </p>
            <p className="font-medium text-danger">
              {selectedError?.title ?? 'นำเข้าไฟล์ไม่สำเร็จ'}
            </p>
            {selectedError?.location && (
              <p className="text-xs text-text">{selectedError.location}</p>
            )}
            {selectedError?.detail && (
              <p className="break-words rounded bg-bg p-2 text-[11px] text-text-muted">
                {selectedError.detail}
              </p>
            )}
          </div>
        )}
      </Modal>
    </div>
  )
}
