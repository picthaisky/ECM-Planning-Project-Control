import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { Button, Modal } from '../../../components'

export interface CaptureBaselineModalProps {
  isOpen: boolean
  onClose: () => void
  busy: boolean
  errorMessage: string | null
  onCapture: (name: string) => void
}

/** S14-FE-01 "บันทึก Baseline ใหม่" dialog — a single required `name`, matching the real
 * `CaptureBaselineCommand`'s only field (`CaptureBaselineRequest(string Name)`). */
export function CaptureBaselineModal({ isOpen, onClose, busy, errorMessage, onCapture }: CaptureBaselineModalProps) {
  const [name, setName] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const inputId = useId()

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) {
      setFieldError('จำเป็นต้องกรอกชื่อ Baseline')
      return
    }
    setFieldError(null)
    onCapture(trimmed)
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="บันทึก Baseline ใหม่">
      <form onSubmit={handleSubmit} noValidate aria-label="บันทึก Baseline ใหม่">
        <label htmlFor={inputId} className="block text-[11px] text-text-faint">
          ชื่อ Baseline
        </label>
        <input
          id={inputId}
          type="text"
          placeholder="เช่น Baseline 1 - อนุมัติสัญญา"
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
        />
        {fieldError && (
          <p role="alert" className="mt-1 text-[10.5px] text-danger">
            {fieldError}
          </p>
        )}
        <p className="mt-2 text-[10.5px] text-text-faint">
          ระบบจะบันทึกวันที่/ระยะเวลา/งบประมาณของทุกกิจกรรมในกำหนดการปัจจุบัน ณ ขณะนี้ เป็น Baseline ชุดใหม่
        </p>

        {errorMessage && (
          <p role="alert" className="mt-3 text-[11px] text-danger">
            {errorMessage}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button type="submit" size="sm" loading={busy}>
            บันทึก Baseline
          </Button>
        </div>
      </form>
    </Modal>
  )
}
