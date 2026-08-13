import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import { buildRecordManpowerLogCorrectionPayload, manpowerLogFormValuesFromEntry, validateManpowerLogFormValues } from '../maneqForm'
import { formatManpowerDate } from '../maneqLabels'
import { useNodeActivities } from '../useNodeActivities'
import { ManpowerLogFormFields } from './ManpowerLogFormFields'
import type { ManpowerLogDto, ManpowerLogEntryKind, RecordManpowerLogCorrectionPayload, WbsNodeOptionDto, WeatherLogOptionDto, WorkCategoryDto } from '../types'

export interface ManpowerCorrectionModalProps {
  isOpen: boolean
  projectId: string
  /** The row being corrected — always the current chain tail returned by the most recent write
   * (this session's own local table, since there is no `GET` list endpoint to re-derive a chain from
   * — see `ManeqPage.tsx`'s remarks). */
  target: ManpowerLogDto
  onClose: () => void
  onSubmit: (logId: string, payload: RecordManpowerLogCorrectionPayload) => void
  busy: boolean
  errorMessage: string | null
  workCategories?: WorkCategoryDto[]
  wbsNodes?: WbsNodeOptionDto[]
  weatherLogs?: WeatherLogOptionDto[]
}

const ENTRY_KIND_OPTIONS: Array<{ value: Extract<ManpowerLogEntryKind, 'Correction' | 'Retraction'>; label: string; hint: string }> = [
  {
    value: 'Correction',
    label: 'แก้ไขข้อมูล (Correction)',
    hint: 'แทนที่ข้อมูลเดิมด้วยข้อมูลใหม่ที่ถูกต้อง — เช่น กรอกจำนวนคน/ชั่วโมงผิด',
  },
  {
    value: 'Retraction',
    label: 'เพิกถอนรายการทั้งหมด (Retraction)',
    hint: 'ยกเลิกรายการเดิมทั้งหมดโดยไม่มีข้อมูลใหม่มาแทนที่ — ใช้เมื่อรายการเดิมไม่ควรมีอยู่เลย (เช่น บันทึกผิดวัน/ผิดโครงการ)',
  },
]

/**
 * "แก้ไข" form (S12-FE-02) — `POST .../manpower-logs/{logId}/corrections`. Per domain-rules.md §4.7
 * rule 6, a correction **replaces**, it does not patch — every field starts pre-filled from
 * `target`'s current values (`manpowerLogFormValuesFromEntry`). Choosing `Retraction` disables the
 * data fields, mirroring `WeatherCorrectionModal`'s identical reasoning.
 */
export function ManpowerCorrectionModal({ isOpen, projectId, target, onClose, onSubmit, busy, errorMessage, workCategories, wbsNodes, weatherLogs }: ManpowerCorrectionModalProps) {
  const [entryKind, setEntryKind] = useState<Extract<ManpowerLogEntryKind, 'Correction' | 'Retraction'>>('Correction')
  const [reason, setReason] = useState('')
  const [values, setValues] = useState(() => manpowerLogFormValuesFromEntry(target))
  const activities = useNodeActivities(projectId, values.wbsNodeId)
  const [validationError, setValidationError] = useState<string | null>(null)
  const reasonId = useId()

  const trimmedReason = reason.trim()
  const reasonInvalid = trimmedReason.length === 0
  const isRetraction = entryKind === 'Retraction'

  function handleClose() {
    setEntryKind('Correction')
    setReason('')
    setValues(manpowerLogFormValuesFromEntry(target))
    setValidationError(null)
    onClose()
  }

  function handleSubmit() {
    if (busy || reasonInvalid) return
    if (!isRetraction) {
      const validation = validateManpowerLogFormValues(values)
      setValidationError(validation)
      if (validation) return
    } else {
      setValidationError(null)
    }

    onSubmit(target.id, buildRecordManpowerLogCorrectionPayload(values, entryKind, trimmedReason))
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`แก้ไขบันทึก — วันที่ ${formatManpowerDate(target.logDate)}`}
      className="max-w-2xl"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button variant={isRetraction ? 'danger' : 'primary'} size="sm" onClick={handleSubmit} loading={busy} disabled={reasonInvalid}>
            {isRetraction ? 'ยืนยันเพิกถอน' : 'บันทึกการแก้ไข'}
          </Button>
        </>
      }
    >
      <p className="rounded-card border border-border bg-bg px-3 py-2 text-[10.5px] text-text-muted">
        รายการเดิมยังถูกเก็บไว้เป็นหลักฐานเสมอ (append-only) — นี่เป็นบันทึกใหม่ที่อ้างอิงรายการเดิม
        และตัวรายการนี้เองก็แก้ไข/ลบไม่ได้อีกเช่นกันหลังบันทึก
      </p>

      <fieldset className="mt-3">
        <legend className="text-[11px] text-text-faint">ประเภทการแก้ไข</legend>
        <div className="mt-1 flex flex-col gap-2">
          {ENTRY_KIND_OPTIONS.map((option) => (
            <label key={option.value} className="flex cursor-pointer items-start gap-2 rounded-card border border-border p-2 text-xs">
              <input
                type="radio"
                name="manpower-entry-kind"
                value={option.value}
                checked={entryKind === option.value}
                onChange={() => setEntryKind(option.value)}
                className="mt-0.5"
              />
              <span>
                <span className="block font-medium text-text">{option.label}</span>
                <span className="block text-[10.5px] text-text-faint">{option.hint}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="mt-3">
        <label htmlFor={reasonId} className="block text-[11px] text-text-faint">
          เหตุผลที่แก้ไข/เพิกถอน (บังคับ)
        </label>
        <textarea
          id={reasonId}
          value={reason}
          maxLength={500}
          onChange={(e) => setReason(e.target.value)}
          placeholder="เช่น ตรวจใบเซ็นชื่อกะแล้ว จำนวนคนจริงคือ 168 ไม่ใช่ 186"
          rows={2}
          className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
        />
        {reasonInvalid && reason.length > 0 && (
          <p role="alert" className="mt-1 text-[10.5px] text-danger">
            กรุณาระบุเหตุผล ห้ามเว้นว่าง
          </p>
        )}
      </div>

      {isRetraction && (
        <p className="mt-3 text-[10.5px] text-text-faint">
          เลือกเพิกถอนแล้ว — ข้อมูลด้านล่างถูกปิดการแก้ไข (ใช้ค่าจากรายการเดิม)
        </p>
      )}
      <div className="mt-2">
        <ManpowerLogFormFields values={values} onChange={(patch) => setValues((prev) => ({ ...prev, ...patch }))} disabled={isRetraction} workCategories={workCategories} wbsNodes={wbsNodes} activities={activities} weatherLogs={weatherLogs} />
      </div>

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
