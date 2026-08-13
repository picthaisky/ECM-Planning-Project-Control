import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import { buildRecordManpowerLogPayload, emptyManpowerLogFormValues, validateManpowerLogFormValues } from '../maneqForm'
import { useNodeActivities } from '../useNodeActivities'
import { ManpowerLogFormFields } from './ManpowerLogFormFields'
import type { RecordManpowerLogPayload, WbsNodeOptionDto, WeatherLogOptionDto, WorkCategoryDto } from '../types'

export interface ManpowerRecordModalProps {
  isOpen: boolean
  projectId: string
  onClose: () => void
  onSubmit: (payload: RecordManpowerLogPayload) => void
  busy: boolean
  errorMessage: string | null
  workCategories?: WorkCategoryDto[]
  wbsNodes?: WbsNodeOptionDto[]
  weatherLogs?: WeatherLogOptionDto[]
}

/**
 * "+ บันทึกวันนี้" form (S12-FE-02 / US-12.2) — `POST .../manpower-logs`.
 *
 * The entity is append-only (domain-rules.md §4.7: no `PUT`/`PATCH`/`DELETE`, ever — the same
 * "claim evidence" reasoning `features/weather` already established for `DailyWeatherLog`), so this
 * form carries the same short immutability notice `WeatherRecordModal` does, without repeating its
 * full mandatory-checkbox gate: a typo here is a same-day operational record, not itself the sole
 * evidence for a schedule-extension claim — the correction endpoint is always the actual remedy
 * either way.
 *
 * **"ยืนยันบันทึกซ้ำ"** — §4.4/Q8's "warn-and-confirm" duplicate rule: an in-force `Original` already
 * existing for the same (date, shift, category, node, labour type, subcontractor) key returns
 * `409 ManpowerLogAlreadyExists` unless the request carries `allowDuplicate: true`. Exposed here as a
 * plain checkbox (default unticked) rather than a silent auto-retry — a genuine second crew on the
 * same key is real and should be recorded, but only when the user actually confirms it.
 */
export function ManpowerRecordModal({ isOpen, projectId, onClose, onSubmit, busy, errorMessage, workCategories, wbsNodes, weatherLogs }: ManpowerRecordModalProps) {
  const [values, setValues] = useState(emptyManpowerLogFormValues)
  const activities = useNodeActivities(projectId, values.wbsNodeId)
  const [allowDuplicate, setAllowDuplicate] = useState(false)
  const [validationError, setValidationError] = useState<string | null>(null)
  const allowDuplicateId = useId()

  function handleClose() {
    setValues(emptyManpowerLogFormValues())
    setAllowDuplicate(false)
    setValidationError(null)
    onClose()
  }

  function handleSubmit() {
    if (busy) return
    const validation = validateManpowerLogFormValues(values)
    setValidationError(validation)
    if (validation) return
    onSubmit(buildRecordManpowerLogPayload(values, allowDuplicate))
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="บันทึกกำลังคน/เครื่องจักรวันนี้"
      className="max-w-2xl"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button variant="primary" size="sm" onClick={handleSubmit} loading={busy}>
            บันทึก
          </Button>
        </>
      }
    >
      <p className="rounded-card border border-border bg-bg px-3 py-2 text-[10.5px] text-text-muted">
        บันทึกนี้แก้ไข/ลบไม่ได้หลังบันทึก (append-only) — หากกรอกผิด ให้ใช้ปุ่ม &quot;แก้ไข&quot; ที่รายการนั้นภายหลัง
        เพื่อบันทึก<span className="font-semibold">รายการแก้ไขใหม่ที่อ้างอิงรายการเดิม</span>
      </p>

      <div className="mt-3">
        <ManpowerLogFormFields values={values} onChange={(patch) => setValues((prev) => ({ ...prev, ...patch }))} workCategories={workCategories} wbsNodes={wbsNodes} activities={activities} weatherLogs={weatherLogs} />
      </div>

      <label htmlFor={allowDuplicateId} className="mt-3 flex items-start gap-2 text-[11px] text-text-muted">
        <input
          id={allowDuplicateId}
          type="checkbox"
          checked={allowDuplicate}
          onChange={(e) => setAllowDuplicate(e.target.checked)}
          className="mt-0.5"
        />
        <span>
          ยืนยันบันทึกซ้ำ — ใช้เมื่อมีทีมงาน/กะอื่นบันทึกไว้แล้วสำหรับวันที่/หมวดงาน/WBS Node เดียวกันจริง (ไม่ใช่การพิมพ์ผิด)
        </span>
      </label>

      {validationError && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {validationError}
        </p>
      )}
      {errorMessage && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {errorMessage}
        </p>
      )}
    </Modal>
  )
}
