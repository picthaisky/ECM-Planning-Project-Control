import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import {
  buildWeatherLogRequestFields,
  emptyWeatherLogFormValues,
  validateWeatherLogFormValues,
} from '../weatherForm'
import { WeatherLogFormFields } from './WeatherLogFormFields'
import type { RecordWeatherLogPayload } from '../types'

export interface WeatherRecordModalProps {
  isOpen: boolean
  onClose: () => void
  onSubmit: (payload: RecordWeatherLogPayload) => void
  busy: boolean
  errorMessage: string | null
}

/**
 * "+ บันทึกวันนี้" form (S11-FE-01 / US-11.1) — `POST .../weather-logs`.
 *
 * **This is the DoD's explicit requirement, met three ways, all before the write happens:**
 * 1. A persistent warning banner, visible for the entire time the form is open (not a surprise at
 *    the last click) — domain-rules.md §8.3: there is no `PUT`/`PATCH`/`DELETE` for this resource at
 *    all; a typo cannot be edited in place, ever.
 * 2. A mandatory acknowledgement checkbox — the "ยืนยันบันทึกแบบถาวร" button stays disabled until it
 *    is checked, so confirming without having seen the warning is not possible by construction.
 * 3. The confirm button's own label repeats it at the exact moment of the irreversible action.
 *
 * The banner also names the actual remedy for "I made a typo" — the correction endpoint — so the
 * warning is not a dead end (the DoD's second explicit ask): a new, separately-audited entry
 * referencing this one (`WeatherLogTable`'s "แก้ไข/ยกเลิกรายการ" row action, §8.2).
 */
export function WeatherRecordModal({ isOpen, onClose, onSubmit, busy, errorMessage }: WeatherRecordModalProps) {
  const [values, setValues] = useState(emptyWeatherLogFormValues)
  const [acknowledged, setAcknowledged] = useState(false)
  const [validationError, setValidationError] = useState<string | null>(null)
  const acknowledgeId = useId()

  function handleClose() {
    setValues(emptyWeatherLogFormValues())
    setAcknowledged(false)
    setValidationError(null)
    onClose()
  }

  function handleSubmit() {
    if (busy || !acknowledged) return
    const validation = validateWeatherLogFormValues(values)
    setValidationError(validation)
    if (validation) return
    onSubmit({ ...buildWeatherLogRequestFields(values) } satisfies RecordWeatherLogPayload)
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="บันทึกสภาพอากาศวันนี้"
      className="max-w-xl"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button variant="primary" size="sm" onClick={handleSubmit} loading={busy} disabled={!acknowledged}>
            ยืนยันบันทึกแบบถาวร (แก้ไขภายหลังไม่ได้)
          </Button>
        </>
      }
    >
      <div role="alert" className="rounded-card border border-danger/30 bg-danger/10 p-3">
        <p className="text-xs font-semibold text-danger">บันทึกสภาพอากาศไม่สามารถแก้ไขหรือลบได้หลังบันทึก</p>
        <p className="mt-1 text-[11px] text-danger/90">
          เมื่อยืนยันบันทึกแล้ว รายการนี้จะเป็นหลักฐานถาวรสำหรับการอ้างอิงสิทธิ์ขยายเวลา (EOT) —
          ระบบไม่มีปุ่มแก้ไขหรือลบสำหรับบันทึกสภาพอากาศเลย หากกรอกข้อมูลผิดพลาด ให้กดปุ่ม
          &quot;แก้ไข/ยกเลิกรายการ&quot; ที่รายการนั้นในตารางภายหลัง เพื่อบันทึก
          <span className="font-semibold"> รายการแก้ไขใหม่ที่อ้างอิงรายการเดิม</span>{' '}
          (รายการเดิมจะยังถูกเก็บไว้เป็นหลักฐานเสมอ ไม่ถูกลบหรือเขียนทับ)
        </p>
        <label htmlFor={acknowledgeId} className="mt-2 flex cursor-pointer items-start gap-2 text-[11px] text-danger">
          <input
            id={acknowledgeId}
            type="checkbox"
            checked={acknowledged}
            onChange={(e) => setAcknowledged(e.target.checked)}
            className="mt-0.5"
          />
          <span>ข้าพเจ้าเข้าใจว่าบันทึกนี้ไม่สามารถแก้ไขหรือลบได้ในภายหลัง</span>
        </label>
      </div>

      <div className="mt-4">
        <WeatherLogFormFields values={values} onChange={(patch) => setValues((prev) => ({ ...prev, ...patch }))} />
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
