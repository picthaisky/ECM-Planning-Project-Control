import { useRef } from 'react'
import type { ChangeEvent } from 'react'
import { cx } from '../../utils/cx'
import { parseImportErrorJson } from './errorTranslation'
import type { ImportUploadState, TemplateDownloadState } from './useImportWizard'
import type { ImportRouteFormat } from './types'

type ImportTone = 'danger' | 'secondary' | 'success'

interface ImportSourceDef {
  routeFormat: ImportRouteFormat
  label: string
  badge: string
  accept: string
  expectedExtensions: string[]
  tone: ImportTone
}

/** The three import rows from docs/ECM Planning Prototype.dc.html screen #2's "นำเข้าข้อมูลแผนงาน
 * (Import)" card. Each row IS the format choice (US-3.1–3.4*) — MSPDI only ever means the XML
 * export, never a binary `.mpp` (ADR-0003), hence the `.xml` accept/label rather than `.mpp`. */
const IMPORT_SOURCES: ImportSourceDef[] = [
  {
    routeFormat: 'xer',
    label: 'Primavera P6 (.XER)',
    badge: 'XER',
    accept: '.xer',
    expectedExtensions: ['.xer'],
    tone: 'danger',
  },
  {
    routeFormat: 'mspdi',
    label: 'Microsoft Project (MSPDI .XML)',
    badge: 'MPP',
    accept: '.xml',
    expectedExtensions: ['.xml'],
    tone: 'secondary',
  },
  {
    routeFormat: 'xlsx',
    label: 'เทมเพลตความคืบหน้า Excel',
    badge: 'XLSX',
    accept: '.xlsx',
    expectedExtensions: ['.xlsx'],
    tone: 'success',
  },
]

const badgeToneClasses: Record<ImportTone, string> = {
  danger: 'bg-danger/10 text-danger',
  secondary: 'bg-secondary/10 text-secondary',
  success: 'bg-success/10 text-success',
}

const rowHoverToneClasses: Record<ImportTone, string> = {
  danger: 'hover:border-danger',
  secondary: 'hover:border-secondary',
  success: 'hover:border-success',
}

function hasExpectedExtension(fileName: string, expected: string[]): boolean {
  const lower = fileName.toLowerCase()
  return expected.some((ext) => lower.endsWith(ext))
}

export interface ImportUploadCardProps {
  upload: ImportUploadState
  onSelectFile: (file: File, routeFormat: ImportRouteFormat) => void
  /** Rejects a file client-side (wrong extension for the row clicked) before it ever reaches
   * `onSelectFile`/the network — e.g. "นามสกุลไฟล์ไม่ตรงกับประเภทที่เลือก (.xer)". */
  onClientValidationError: (message: string) => void
  onReset: () => void
  onDownloadTemplate: () => void
  templateDownloadState: TemplateDownloadState
  templateDownloadError: string | null
}

/**
 * Upload step + inline progress/results step (S3-FE-01). Matches the prototype's dashed-row Import
 * card; the progress and results/errors steps are shown inline on/under the row that was clicked
 * rather than as separate full-screen steps, to fit the compact card layout and mobile-first
 * one-hand flow (site engineers on phones/tablets).
 */
export function ImportUploadCard({
  upload,
  onSelectFile,
  onClientValidationError,
  onReset,
  onDownloadTemplate,
  templateDownloadState,
  templateDownloadError,
}: ImportUploadCardProps) {
  const inputRefs = useRef<Partial<Record<ImportRouteFormat, HTMLInputElement | null>>>({})
  const busy = upload.phase === 'uploading' || upload.phase === 'polling'

  function handleRowClick(routeFormat: ImportRouteFormat) {
    if (busy) return
    inputRefs.current[routeFormat]?.click()
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>, source: ImportSourceDef) {
    const file = event.target.files?.[0]
    event.target.value = '' // allow re-selecting the same file name after a failed attempt

    if (!file) return

    if (!hasExpectedExtension(file.name, source.expectedExtensions)) {
      onClientValidationError(
        `นามสกุลไฟล์ไม่ตรงกับประเภทที่เลือก (ต้องเป็น ${source.expectedExtensions.join(', ')})`,
      )
      return
    }

    onSelectFile(file, source.routeFormat)
  }

  const parsedError =
    upload.phase === 'done' && upload.job?.status === 'Failed'
      ? parseImportErrorJson(upload.job.errorJson)
      : null

  const showResultBanner =
    upload.phase === 'done' && (upload.job !== null || upload.requestError !== null)
  const isFailureBanner = upload.requestError !== null || upload.job?.status === 'Failed'

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="mb-3 font-heading text-[13.5px] font-semibold text-navy">
        นำเข้าข้อมูลแผนงาน (Import)
      </div>

      <div className="flex flex-col gap-2 text-xs">
        {IMPORT_SOURCES.map((source) => {
          const isActiveRow = upload.routeFormat === source.routeFormat && busy

          return (
            <div key={source.routeFormat}>
              <button
                type="button"
                disabled={busy}
                onClick={() => handleRowClick(source.routeFormat)}
                className={cx(
                  'flex w-full items-center gap-2.5 rounded-[7px] border border-dashed border-border-subtle px-3 py-2.5 text-left',
                  busy
                    ? 'cursor-not-allowed opacity-60'
                    : cx('cursor-pointer', rowHoverToneClasses[source.tone]),
                )}
              >
                <span
                  aria-hidden="true"
                  className={cx(
                    'grid h-[26px] w-[26px] shrink-0 place-items-center rounded-[5px] text-[9px] font-bold',
                    badgeToneClasses[source.tone],
                  )}
                >
                  {source.badge}
                </span>
                <span className="text-text">{source.label}</span>
                <span className="ml-auto flex items-center gap-1.5 text-[10.5px] text-text-faint">
                  {isActiveRow && (
                    <span
                      aria-hidden="true"
                      className="h-3 w-3 animate-spin rounded-full border-2 border-current border-t-transparent text-navy"
                    />
                  )}
                  {isActiveRow
                    ? upload.phase === 'uploading'
                      ? 'กำลังอัปโหลด...'
                      : 'กำลังตรวจสอบสถานะ...'
                    : 'เลือกไฟล์'}
                </span>
              </button>
              <input
                ref={(el) => {
                  inputRefs.current[source.routeFormat] = el
                }}
                type="file"
                accept={source.accept}
                className="hidden"
                aria-label={source.label}
                disabled={busy}
                onChange={(event) => handleFileChange(event, source)}
              />
            </div>
          )
        })}
      </div>

      <button
        type="button"
        onClick={onDownloadTemplate}
        disabled={templateDownloadState === 'loading'}
        className="mt-3 text-[11px] font-medium text-navy underline decoration-dotted hover:text-navy/80 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {templateDownloadState === 'loading'
          ? 'กำลังดาวน์โหลดเทมเพลต...'
          : 'ดาวน์โหลดเทมเพลตความคืบหน้า (.xlsx)'}
      </button>
      {templateDownloadState === 'error' && (
        <p role="alert" className="mt-1 text-[10.5px] text-danger">
          {templateDownloadError}
        </p>
      )}

      {showResultBanner && (
        <div
          role={isFailureBanner ? 'alert' : 'status'}
          className={cx(
            'mt-3 rounded-card border p-3 text-xs',
            isFailureBanner
              ? 'border-danger/30 bg-danger/5 text-danger'
              : 'border-success/30 bg-success/5 text-success',
          )}
        >
          {upload.requestError ? (
            <p className="font-medium">{upload.requestError}</p>
          ) : upload.job?.status === 'Succeeded' ? (
            <p className="font-medium">
              นำเข้าสำเร็จ: {upload.fileName} · {upload.job.rowsImported.toLocaleString('th-TH')}{' '}
              รายการ
            </p>
          ) : (
            <div>
              <p className="font-medium">{parsedError?.title ?? 'นำเข้าไฟล์ไม่สำเร็จ'}</p>
              {parsedError?.location && <p className="mt-1 text-[11px]">{parsedError.location}</p>}
              {parsedError?.detail && (
                <p className="mt-1 break-words text-[10.5px] text-danger/80">
                  {parsedError.detail}
                </p>
              )}
            </div>
          )}
          <button
            type="button"
            onClick={onReset}
            className="mt-2 text-[10.5px] font-medium underline decoration-dotted"
          >
            นำเข้าไฟล์อื่น
          </button>
        </div>
      )}
    </div>
  )
}
