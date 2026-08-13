import { useState, type FormEvent } from 'react'
import { Button, Modal } from '../../../components'
import { createPaymentCertificate, PaymentApiError } from '../api'
import type { PaymentCertificateDto } from '../types'

const inputClass =
  'mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none'
const labelClass = 'block text-[11px] text-text-faint'

export interface CreateCertificateModalProps {
  projectId: string
  isOpen: boolean
  onClose: () => void
  /** Called with the created certificate so the caller can upsert + select it (no list refetch). */
  onCreated: (created: PaymentCertificateDto) => void
}

/**
 * S9-FE (create): raises one period's IPC. The server computes every money field (gross/retention/
 * advance/net) from the project's configured rates and auto-derives the previous cumulative, so this
 * form only collects what the QS certifies - the milestone, its value, and the cumulative % this
 * period. A missing retention/advance rate surfaces as the server's 422 (rendered inline), not a
 * client-side guess.
 */
export function CreateCertificateModal({ projectId, isOpen, onClose, onCreated }: CreateCertificateModalProps) {
  const [milestoneNo, setMilestoneNo] = useState('1')
  const [description, setDescription] = useState('')
  const [milestoneValue, setMilestoneValue] = useState('')
  const [thisCumulativeApprovePct, setThisCumulativeApprovePct] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reset = () => {
    setMilestoneNo('1')
    setDescription('')
    setMilestoneValue('')
    setThisCumulativeApprovePct('')
    setError(null)
  }

  const handleClose = () => {
    reset()
    onClose()
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      const created = await createPaymentCertificate(projectId, {
        milestoneNo: Number(milestoneNo),
        description: description.trim() ? description.trim() : null,
        milestoneValue: Number(milestoneValue),
        thisCumulativeApprovePct: Number(thisCumulativeApprovePct),
      })
      onCreated(created)
      reset()
      onClose()
    } catch (err) {
      setError(err instanceof PaymentApiError ? err.message : 'สร้างใบรับรองผลงานไม่สำเร็จ')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title="สร้างใบรับรองผลงาน (IPC)" className="max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor="ipc-milestone-no" className={labelClass}>
              งวดที่ (Milestone No.)
            </label>
            <input
              id="ipc-milestone-no"
              className={inputClass}
              type="number"
              min={1}
              step={1}
              required
              value={milestoneNo}
              onChange={(e) => setMilestoneNo(e.target.value)}
            />
          </div>
          <div>
            <label htmlFor="ipc-approve-pct" className={labelClass}>
              % ที่รับรองสะสมงวดนี้
            </label>
            <input
              id="ipc-approve-pct"
              className={inputClass}
              type="number"
              min={0}
              max={100}
              step="0.01"
              required
              value={thisCumulativeApprovePct}
              onChange={(e) => setThisCumulativeApprovePct(e.target.value)}
            />
          </div>
        </div>

        <div>
          <label htmlFor="ipc-milestone-value" className={labelClass}>
            มูลค่างวดงาน (Milestone Value)
          </label>
          <input
            id="ipc-milestone-value"
            className={inputClass}
            type="number"
            min={0}
            step="0.01"
            required
            value={milestoneValue}
            onChange={(e) => setMilestoneValue(e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="ipc-description" className={labelClass}>
            รายละเอียด (ไม่บังคับ)
          </label>
          <input
            id="ipc-description"
            className={inputClass}
            type="text"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>

        <p className="text-[11px] text-text-faint">
          ระบบจะคำนวณ Retention / Advance / ยอดจ่ายสุทธิ ให้อัตโนมัติจากอัตราที่ตั้งไว้ในโครงการ
        </p>

        {error && (
          <p role="alert" className="text-xs text-danger">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={handleClose} disabled={submitting}>
            ยกเลิก
          </Button>
          <Button type="submit" size="sm" loading={submitting}>
            สร้างใบรับรอง
          </Button>
        </div>
      </form>
    </Modal>
  )
}
