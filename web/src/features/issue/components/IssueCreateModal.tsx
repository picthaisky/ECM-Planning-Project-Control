import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import { toIssueRequestDate } from '../issueLabels'
import type { CreateIssuePayload } from '../types'

export interface IssueCreateModalProps {
  isOpen: boolean
  onClose: () => void
  onSubmit: (payload: CreateIssuePayload) => void
  busy: boolean
  errorMessage: string | null
}

interface FormValues {
  title: string
  detail: string
  owner: string
  dueDate: string
}

const EMPTY_VALUES: FormValues = { title: '', detail: '', owner: '', dueDate: '' }

/** "+ แจ้งปัญหาใหม่" form (S11-FE-01 / US-11.2) — `POST .../issues`. Always created `Status = Open`
 * server-side; there is no status field here. Mirrors `features/vo/components/CancelVoModal.tsx`'s
 * established confirm-with-fields dialog shape. */
export function IssueCreateModal({ isOpen, onClose, onSubmit, busy, errorMessage }: IssueCreateModalProps) {
  const [values, setValues] = useState<FormValues>(EMPTY_VALUES)
  const [validationError, setValidationError] = useState<string | null>(null)
  const titleId = useId()
  const detailId = useId()
  const ownerId = useId()
  const dueDateId = useId()

  function handleClose() {
    setValues(EMPTY_VALUES)
    setValidationError(null)
    onClose()
  }

  function handleSubmit() {
    if (busy) return
    const title = values.title.trim()
    if (!title) {
      setValidationError('กรุณาระบุหัวข้อปัญหา')
      return
    }
    setValidationError(null)
    onSubmit({
      title,
      detail: values.detail.trim() === '' ? null : values.detail.trim(),
      owner: values.owner.trim() === '' ? null : values.owner.trim(),
      dueDate: toIssueRequestDate(values.dueDate),
    })
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="แจ้งปัญหาใหม่"
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
      <div className="flex flex-col gap-3">
        <div>
          <label htmlFor={titleId} className="block text-[11px] text-text-faint">
            หัวข้อปัญหา (บังคับ)
          </label>
          <input
            id={titleId}
            type="text"
            maxLength={200}
            value={values.title}
            onChange={(e) => setValues((prev) => ({ ...prev, title: e.target.value }))}
            placeholder="เช่น น้ำรั่วซึมผนัง Basement โซน B"
            className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
          />
        </div>

        <div>
          <label htmlFor={detailId} className="block text-[11px] text-text-faint">
            รายละเอียด (ไม่บังคับ)
          </label>
          <textarea
            id={detailId}
            maxLength={2000}
            rows={3}
            value={values.detail}
            onChange={(e) => setValues((prev) => ({ ...prev, detail: e.target.value }))}
            className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor={ownerId} className="block text-[11px] text-text-faint">
              ผู้รับผิดชอบ (ไม่บังคับ)
            </label>
            <input
              id={ownerId}
              type="text"
              maxLength={200}
              value={values.owner}
              onChange={(e) => setValues((prev) => ({ ...prev, owner: e.target.value }))}
              className="mt-1 w-full rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none"
            />
          </div>
          <div>
            <label htmlFor={dueDateId} className="block text-[11px] text-text-faint">
              กำหนดแก้ไข (ไม่บังคับ)
            </label>
            <input
              id={dueDateId}
              type="date"
              value={values.dueDate}
              onChange={(e) => setValues((prev) => ({ ...prev, dueDate: e.target.value }))}
              className="mt-1 w-full rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none"
            />
          </div>
        </div>
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
