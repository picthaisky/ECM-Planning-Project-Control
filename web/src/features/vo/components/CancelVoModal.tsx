import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'

export interface CancelVoModalProps {
  isOpen: boolean
  voNumber: string
  onCancelModal: () => void
  onConfirm: (comment: string) => void
  busy: boolean
  errorMessage: string | null
}

/**
 * Confirm-with-comment dialog for `Cancel` — a `Draft`-only, terminal action distinct from
 * `ApprovalActionModal`'s three approval-chain decisions (Cancel never enters the approval chain at
 * all, domain-rules.md §2.3). Comment is mandatory ("a cancelled instruction is itself evidence").
 */
export function CancelVoModal({ isOpen, voNumber, onCancelModal, onConfirm, busy, errorMessage }: CancelVoModalProps) {
  const [comment, setComment] = useState('')
  const textareaId = useId()
  const trimmed = comment.trim()
  const invalid = trimmed.length === 0

  function handleClose() {
    setComment('')
    onCancelModal()
  }

  function handleConfirm() {
    if (invalid || busy) return
    onConfirm(trimmed)
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`ยกเลิก ${voNumber}`}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={busy}>
            ปิด
          </Button>
          <Button variant="danger" size="sm" onClick={handleConfirm} loading={busy} disabled={invalid}>
            ยืนยันยกเลิก
          </Button>
        </>
      }
    >
      <p className="text-xs text-text-muted">
        เอกสารฉบับร่างนี้จะถูกยกเลิกอย่างถาวร (สถานะ Cancelled) — เอกสารที่ยกเลิกแล้วยังคงเก็บไว้เป็นหลักฐาน แต่ไม่สามารถส่งขออนุมัติได้อีก
        กรุณาระบุเหตุผล (บังคับ)
      </p>

      <label htmlFor={textareaId} className="mt-3 block text-[11px] text-text-faint">
        เหตุผล (บังคับ)
      </label>
      <textarea
        id={textareaId}
        value={comment}
        maxLength={2000}
        onChange={(e) => setComment(e.target.value)}
        placeholder="เหตุผลที่ยกเลิก (บังคับกรอก)"
        rows={3}
        className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
      />
      {invalid && comment.length > 0 && (
        <p role="alert" className="mt-1 text-[10.5px] text-danger">
          กรุณาระบุเหตุผล ห้ามเว้นว่าง
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
