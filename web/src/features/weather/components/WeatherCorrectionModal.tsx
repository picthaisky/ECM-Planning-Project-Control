import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import {
  buildWeatherLogRequestFields,
  validateWeatherLogFormValues,
  weatherLogFormValuesFromEntry,
} from '../weatherForm'
import { formatWeatherDate } from '../weatherLabels'
import { WeatherLogFormFields } from './WeatherLogFormFields'
import type { RecordWeatherLogCorrectionPayload, WeatherLogDto, WeatherLogEntryKind } from '../types'

export interface WeatherCorrectionModalProps {
  isOpen: boolean
  /** The current chain-tail entry being corrected — always the row `WeatherLogTable` offered the
   * action on (`isChainTail` per `weatherChain.ts`), never an arbitrary id. The caller should
   * conditionally render this component (`{target && <WeatherCorrectionModal target={target} .../>}`,
   * mirroring `features/vo/VoPage.tsx#VoContentModal`'s edit-mode usage) so a different target
   * always remounts fresh initial values, rather than requiring this component to react to prop
   * changes mid-session. */
  target: WeatherLogDto
  onClose: () => void
  onSubmit: (logId: string, payload: RecordWeatherLogCorrectionPayload) => void
  busy: boolean
  errorMessage: string | null
}

const ENTRY_KIND_OPTIONS: Array<{ value: Extract<WeatherLogEntryKind, 'Correction' | 'Retraction'>; label: string; hint: string }> = [
  {
    value: 'Correction',
    label: 'แก้ไขข้อมูล (Correction)',
    hint: 'แทนที่ข้อมูลเดิมด้วยข้อมูลใหม่ที่ถูกต้อง — เช่น กรอกจำนวนชั่วโมง/ปริมาณฝนผิด',
  },
  {
    value: 'Retraction',
    label: 'เพิกถอนรายการทั้งหมด (Retraction)',
    hint: 'ยกเลิกรายการเดิมทั้งหมดโดยไม่มีข้อมูลใหม่มาแทนที่ — ใช้เมื่อรายการเดิมไม่ควรมีอยู่เลย (เช่น บันทึกผิดวัน/ผิดโครงการ)',
  },
]

/**
 * "แก้ไข/ยกเลิกรายการ" form (S11-FE-01) — `POST .../weather-logs/{logId}/corrections`. This is the
 * discoverable remedy `WeatherRecordModal`'s warning banner points at: "I made a typo" has an
 * obvious answer here, rather than feeling like a dead end.
 *
 * Per domain-rules.md §8.2 rule 6, a correction **replaces**, it does not patch — every field starts
 * pre-filled from `target`'s current values (`weatherLogFormValuesFromEntry`) so the user edits only
 * what is wrong, but the full set is re-submitted regardless. Choosing `Retraction` disables the data
 * fields (a retraction voids the entry outright; editing values that will never be read is
 * misleading), but the underlying values still round-trip in the request since the DTO shape does
 * not make them conditionally optional server-side.
 *
 * **Equally irreversible, warned the same way**: this correction/retraction row is itself a new,
 * permanent, unmodifiable entry (§8.3 applies to it too) — the same banner + mandatory
 * acknowledgement pattern as `WeatherRecordModal`, not a lighter-touch confirmation just because
 * this is the "fix it" path.
 */
export function WeatherCorrectionModal({ isOpen, target, onClose, onSubmit, busy, errorMessage }: WeatherCorrectionModalProps) {
  const [entryKind, setEntryKind] = useState<Extract<WeatherLogEntryKind, 'Correction' | 'Retraction'>>('Correction')
  const [reason, setReason] = useState('')
  const [values, setValues] = useState(() => weatherLogFormValuesFromEntry(target))
  const [acknowledged, setAcknowledged] = useState(false)
  const [validationError, setValidationError] = useState<string | null>(null)
  const reasonId = useId()
  const acknowledgeId = useId()

  const trimmedReason = reason.trim()
  const reasonInvalid = trimmedReason.length === 0
  const isRetraction = entryKind === 'Retraction'

  function handleClose() {
    setEntryKind('Correction')
    setReason('')
    setValues(weatherLogFormValuesFromEntry(target))
    setAcknowledged(false)
    setValidationError(null)
    onClose()
  }

  function handleSubmit() {
    if (busy || !acknowledged || reasonInvalid) return
    if (!isRetraction) {
      const validation = validateWeatherLogFormValues(values)
      setValidationError(validation)
      if (validation) return
    } else {
      setValidationError(null)
    }

    onSubmit(target.id, {
      entryKind,
      correctionReason: trimmedReason,
      ...buildWeatherLogRequestFields(values),
    } satisfies RecordWeatherLogCorrectionPayload)
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`แก้ไข/ยกเลิกรายการ — บันทึกวันที่ ${formatWeatherDate(target.logDate)}`}
      className="max-w-xl"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button
            variant={isRetraction ? 'danger' : 'primary'}
            size="sm"
            onClick={handleSubmit}
            loading={busy}
            disabled={!acknowledged || reasonInvalid}
          >
            {isRetraction ? 'ยืนยันเพิกถอนแบบถาวร' : 'ยืนยันบันทึกการแก้ไขแบบถาวร'}
          </Button>
        </>
      }
    >
      <div role="alert" className="rounded-card border border-danger/30 bg-danger/10 p-3">
        <p className="text-xs font-semibold text-danger">รายการแก้ไข/เพิกถอนนี้ก็ไม่สามารถแก้ไขหรือลบได้เช่นกัน</p>
        <p className="mt-1 text-[11px] text-danger/90">
          รายการเดิม (วันที่ {formatWeatherDate(target.logDate)}) จะยังถูกเก็บไว้เป็นหลักฐานเสมอ ไม่ถูกลบหรือเขียนทับ —
          รายการนี้เป็นเพียงบันทึกใหม่ที่อ้างอิงรายการเดิม และตัวรายการแก้ไขนี้เองก็จะแก้ไขหรือลบไม่ได้อีกเช่นกันหลังยืนยัน
        </p>
        <label htmlFor={acknowledgeId} className="mt-2 flex cursor-pointer items-start gap-2 text-[11px] text-danger">
          <input
            id={acknowledgeId}
            type="checkbox"
            checked={acknowledged}
            onChange={(e) => setAcknowledged(e.target.checked)}
            className="mt-0.5"
          />
          <span>ข้าพเจ้าเข้าใจว่ารายการนี้ไม่สามารถแก้ไขหรือลบได้ในภายหลัง</span>
        </label>
      </div>

      <fieldset className="mt-4">
        <legend className="text-[11px] text-text-faint">ประเภทการแก้ไข</legend>
        <div className="mt-1 flex flex-col gap-2">
          {ENTRY_KIND_OPTIONS.map((option) => (
            <label key={option.value} className="flex cursor-pointer items-start gap-2 rounded-card border border-border p-2 text-xs">
              <input
                type="radio"
                name="entry-kind"
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
          placeholder="เช่น ตรวจใบบันทึกกะแล้ว หยุดงานจริง 3 ชั่วโมง"
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
          เลือกเพิกถอนแล้ว — ข้อมูลด้านล่างถูกปิดการแก้ไข (ใช้ค่าจากรายการเดิม) เนื่องจากรายการเพิกถอนไม่มีเนื้อหาใหม่มาแทนที่
        </p>
      )}
      <div className="mt-2">
        <WeatherLogFormFields
          values={values}
          onChange={(patch) => setValues((prev) => ({ ...prev, ...patch }))}
          disabled={isRetraction}
        />
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
