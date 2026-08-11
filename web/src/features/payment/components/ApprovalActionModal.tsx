import { useId, useState } from 'react'
import { Button, Modal } from '../../../components'
import type { ButtonVariant } from '../../../components'

export type ApprovalActionModalKind = 'approve' | 'return' | 'reject'

export interface ApprovalActionModalProps {
  kind: ApprovalActionModalKind
  isOpen: boolean
  onCancel: () => void
  onConfirm: (comment: string) => void
  busy: boolean
  errorMessage: string | null
}

const COMMENT_MAX_LENGTH = 2000 // ApprovalActionConfiguration.Comment HasMaxLength(2000)

interface KindConfig {
  title: string
  description: string
  /** Mirrors the server's own validators exactly: `ApprovePaymentCertificateCommandValidator`
   * leaves `Comment` optional (max length only); `Return`/`RejectPaymentCertificateCommandValidator`
   * both require `NotEmpty()` (approval-workflow.md §4 rule 2). Enforced here too so the rejection
   * is never a surprise round trip — not because the client is the authority (it never is). */
  commentRequired: boolean
  confirmLabel: string
  confirmVariant: ButtonVariant
  placeholder: string
}

const KIND_CONFIG: Record<ApprovalActionModalKind, KindConfig> = {
  approve: {
    title: 'ยืนยันการอนุมัติ',
    // Deliberately document-type-agnostic ("เอกสารฉบับนี้", not "ใบรับรองผลงานฉบับนี้") — this modal
    // is shared by Payment Certificate (S9-FE-02) and Variation Order (S10-FE-01) via the now
    // document-agnostic `ApprovalChainBar`; see that component's own remarks.
    description: 'คุณกำลังอนุมัติเอกสารฉบับนี้ในขั้นตอนของคุณ ความเห็นเป็นทางเลือก',
    commentRequired: false,
    confirmLabel: 'อนุมัติ',
    confirmVariant: 'primary',
    placeholder: 'ความเห็นเพิ่มเติม (ไม่บังคับ)',
  },
  return: {
    title: 'ตีกลับเพื่อแก้ไข',
    // Deliberately spells out "ไม่ใช่การปฏิเสธ" — the exact confusion the prototype's own "ตีกลับ"
    // badge caused by mapping to Rejected (S9-FE-02 DoD) must not be reintroduced by ambiguous copy.
    description:
      'เอกสารจะถูกส่งกลับเป็นฉบับร่าง (Draft) เพื่อแก้ไข — ไม่ใช่การปฏิเสธ การอนุมัติที่เก็บไว้ในรอบนี้ทั้งหมดจะถูกล้าง และต้องส่งขออนุมัติใหม่ตั้งแต่ขั้นตอนแรก กรุณาระบุเหตุผล (บังคับ)',
    commentRequired: true,
    confirmLabel: 'ยืนยันตีกลับ',
    confirmVariant: 'secondary',
    placeholder: 'เหตุผลที่ตีกลับ (บังคับกรอก)',
  },
  reject: {
    title: 'ปฏิเสธเอกสาร (Reject)',
    description:
      'การปฏิเสธเป็นการยุติเอกสารฉบับนี้อย่างถาวร ไม่สามารถย้อนกลับหรือแก้ไขต่อได้ — ต่างจาก "ตีกลับ" ที่ยังกลับไปแก้ไขได้ กรุณาระบุเหตุผล (บังคับ)',
    commentRequired: true,
    confirmLabel: 'ยืนยันปฏิเสธ',
    confirmVariant: 'danger',
    placeholder: 'เหตุผลที่ปฏิเสธ (บังคับกรอก)',
  },
}

/**
 * Shared confirm-with-comment dialog for the three approval decisions (S9-FE-02 DoD: "ตีกลับ" =
 * `ReturnForRevision`, never `Rejected`; comment mandatory on both return and reject, optional on
 * approve). One component, three configurations (`KIND_CONFIG`) rather than three near-duplicate
 * modals, so the copy distinguishing "ตีกลับ" from "ปฏิเสธ" lives in exactly one place — matches
 * `features/wbs/DecreaseConfirmModal.tsx`'s established confirm-modal pattern.
 *
 * Document-type-agnostic (S10-FE-01 reuses it unchanged for Variation Order — same trap, same fix:
 * ADR-0016 widens the reject-quorum rule the "ปฏิเสธ" copy below already warns is terminal).
 */
export function ApprovalActionModal({ kind, isOpen, onCancel, onConfirm, busy, errorMessage }: ApprovalActionModalProps) {
  const [comment, setComment] = useState('')
  const textareaId = useId()
  const config = KIND_CONFIG[kind]

  const trimmed = comment.trim()
  const commentInvalid = config.commentRequired && trimmed.length === 0

  function handleCancel() {
    setComment('')
    onCancel()
  }

  function handleConfirm() {
    if (commentInvalid || busy) return
    onConfirm(trimmed)
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleCancel}
      title={config.title}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleCancel} disabled={busy}>
            ยกเลิก
          </Button>
          <Button
            variant={config.confirmVariant}
            size="sm"
            onClick={handleConfirm}
            loading={busy}
            disabled={commentInvalid}
          >
            {config.confirmLabel}
          </Button>
        </>
      }
    >
      <p className="text-xs text-text-muted">{config.description}</p>

      <label htmlFor={textareaId} className="mt-3 block text-[11px] text-text-faint">
        ความเห็น{config.commentRequired ? ' (บังคับ)' : ' (ไม่บังคับ)'}
      </label>
      <textarea
        id={textareaId}
        value={comment}
        maxLength={COMMENT_MAX_LENGTH}
        onChange={(e) => setComment(e.target.value)}
        placeholder={config.placeholder}
        rows={3}
        className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
      />
      {commentInvalid && comment.length > 0 && (
        <p role="alert" className="mt-1 text-[10.5px] text-danger">
          กรุณาระบุความเห็น ห้ามเว้นว่าง
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
