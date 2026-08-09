import { useState } from 'react'
import { Button, StatusPill } from '../../../components'
import { cx } from '../../../utils/cx'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import {
  canAttemptApprove,
  canAttemptReject,
  canAttemptReturnForRevision,
  resolveChainStepTone,
} from '../chainPermissions'
import { PAYMENT_STATUS_LABELS } from '../paymentStatusLabels'
import { ApprovalActionModal } from './ApprovalActionModal'
import type { ApprovalActionModalKind } from './ApprovalActionModal'
import type { CertificateActionKind } from '../usePaymentCertificateActions'
import type { ApprovalActionsState } from '../useApprovalActions'
import type { ApprovalActionDto, PaymentCertificateDto } from '../types'

const dateTimeFormatter = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

function formatActedAt(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : dateTimeFormatter.format(parsed)
}

const ACTION_TYPE_LABELS: Record<ApprovalActionDto['action'], string> = {
  Submit: 'ยื่นขออนุมัติ',
  Approve: 'อนุมัติ',
  ReturnForRevision: 'ตีกลับแก้ไข',
  Reject: 'ปฏิเสธ',
  Withdraw: 'ถอนคำขอ',
  Cancel: 'ยกเลิกเอกสาร',
  RecordPayment: 'บันทึกการจ่ายเงิน',
}

const STEP_TONE_CLASS: Record<string, string> = {
  done: 'border-success bg-success text-white',
  current: 'border-gold bg-gold text-navy',
  pending: 'border-border bg-surface text-text-faint',
  rejected: 'border-danger bg-danger text-white',
}

export interface ApprovalChainBarProps {
  certificate: PaymentCertificateDto
  history: ApprovalActionDto[] | null
  historyState: ApprovalActionsState
  historyUnavailableReason: string | null
  currentUserId: string | null
  busyAction: CertificateActionKind | null
  actionError: string | null
  quorumPendingNotice: boolean
  onApprove: (comment?: string) => Promise<PaymentCertificateDto | null>
  onReturnForRevision: (comment: string) => Promise<PaymentCertificateDto | null>
  onReject: (comment: string) => Promise<PaymentCertificateDto | null>
}

/**
 * S9-FE-02: the approval-chain status bar. Shows every step position, the current step, and the
 * append-only history (role/approver/time/comment) when it can be loaded — see `types.ts`'s and
 * `api.ts#getApprovalActions`'s remarks for why that history is not always available today (the
 * backing endpoint is documented but not yet implemented server-side). Buttons render only for
 * actions the current user might plausibly be entitled to (`chainPermissions.ts`) — the server
 * remains the actual authorization boundary in every case.
 *
 * "ตีกลับ" (`ReturnForRevision`) and "ปฏิเสธ" (`Reject`) are two distinct, clearly-labelled buttons
 * that open two distinctly-worded confirmation modals (`ApprovalActionModal`) — never conflated, the
 * exact mistake the prototype's own "ตีกลับ" badge made by mapping to `Rejected`.
 */
export function ApprovalChainBar({
  certificate,
  history,
  historyState,
  historyUnavailableReason,
  currentUserId,
  busyAction,
  actionError,
  quorumPendingNotice,
  onApprove,
  onReturnForRevision,
  onReject,
}: ApprovalChainBarProps) {
  const [openModal, setOpenModal] = useState<ApprovalActionModalKind | null>(null)

  const notSubmittedYet = certificate.status === 'NotDue' || certificate.status === 'Draft'
  const isBusy = busyAction !== null

  async function handleConfirm(comment: string) {
    const action =
      openModal === 'approve' ? onApprove : openModal === 'return' ? onReturnForRevision : onReject
    const result = await action(comment)
    if (result) setOpenModal(null) // keep the modal open (with the error shown) on failure
  }

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-heading text-sm font-semibold text-navy">เส้นทางการอนุมัติ (Approval Chain)</h3>
        <StatusPill label={PAYMENT_STATUS_LABELS[certificate.status]} status={certificate.status} />
      </div>

      {notSubmittedYet ? (
        <p className="mt-3 text-xs text-text-faint">ยังไม่ได้ส่งขออนุมัติเอกสารฉบับนี้</p>
      ) : (
        <>
          <div className="mt-3 flex items-center gap-1.5" role="list" aria-label="ขั้นตอนการอนุมัติ">
            {Array.from({ length: certificate.totalSteps }, (_, i) => i + 1).map((stepNo) => {
              const tone = resolveChainStepTone(certificate, stepNo)
              return (
                <div key={stepNo} role="listitem" className="flex items-center gap-1.5">
                  <span
                    aria-label={`ขั้นตอนที่ ${stepNo}${stepNo === certificate.currentStepNo ? ' (ปัจจุบัน)' : ''}`}
                    className={cx(
                      'grid h-7 w-7 flex-none place-items-center rounded-full border text-[11px] font-semibold',
                      STEP_TONE_CLASS[tone],
                    )}
                  >
                    {stepNo}
                  </span>
                  {stepNo < certificate.totalSteps && (
                    <span aria-hidden="true" className="h-px w-6 bg-border" />
                  )}
                </div>
              )
            })}
            <span className="ml-2 text-[11px] text-text-faint">
              ขั้นตอนที่ {certificate.currentStepNo} จาก {certificate.totalSteps}
            </span>
          </div>

          {quorumPendingNotice && (
            <p role="status" className="mt-3 rounded border border-warning-text/30 bg-warning-text/10 px-3 py-2 text-[11px] text-warning-text">
              บันทึกการอนุมัติของคุณแล้ว แต่ขั้นตอนที่ {certificate.currentStepNo} ยังต้องการผู้อนุมัติเพิ่มเติมให้ครบตาม
              Quorum ที่กำหนดไว้ — สถานะเอกสารยังคงเป็น &ldquo;{PAYMENT_STATUS_LABELS[certificate.status]}&rdquo;
            </p>
          )}

          <div className="mt-3">
            {historyState === 'loading' && (
              <p className="text-[11px] text-text-faint">กำลังโหลดประวัติการอนุมัติ...</p>
            )}
            {historyState === 'unavailable' && (
              <p className="rounded border border-dashed border-border px-3 py-2 text-[11px] text-text-faint">
                ยังไม่สามารถโหลดรายละเอียดผู้อนุมัติ/เวลา/ความเห็นของแต่ละขั้นตอนได้ในขณะนี้
                {historyUnavailableReason ? ` (${historyUnavailableReason})` : ''}
              </p>
            )}
            {historyState === 'ready' && history && (
              <ul className="space-y-1.5">
                {history.map((action) => (
                  <li
                    key={action.id}
                    className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5 border-t border-border-subtle pt-1.5 text-[11.5px] first:border-t-0 first:pt-0"
                  >
                    <span className="text-text">
                      <span className="font-semibold text-navy">{ACTION_TYPE_LABELS[action.action]}</span>
                      {action.stepNo > 0 && <span className="text-text-faint"> · ขั้นตอนที่ {action.stepNo}</span>}
                      <span className="text-text-faint"> · {ROLE_LABEL[action.actorRoleAtTime]}</span>
                    </span>
                    <span className="text-text-faint">{formatActedAt(action.actedAt)}</span>
                    {action.comment && <span className="w-full text-text-muted">&ldquo;{action.comment}&rdquo;</span>}
                  </li>
                ))}
                {history.length === 0 && <li className="text-[11px] text-text-faint">ยังไม่มีการดำเนินการ</li>}
              </ul>
            )}
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            {canAttemptApprove(certificate, currentUserId) && (
              <Button size="sm" onClick={() => setOpenModal('approve')} disabled={isBusy}>
                อนุมัติ
              </Button>
            )}
            {canAttemptReturnForRevision(certificate) && (
              <Button size="sm" variant="secondary" onClick={() => setOpenModal('return')} disabled={isBusy}>
                ตีกลับแก้ไข
              </Button>
            )}
            {canAttemptReject(certificate) && (
              <Button size="sm" variant="danger" onClick={() => setOpenModal('reject')} disabled={isBusy}>
                ปฏิเสธ
              </Button>
            )}
          </div>
        </>
      )}

      {openModal && (
        <ApprovalActionModal
          kind={openModal}
          isOpen
          onCancel={() => setOpenModal(null)}
          onConfirm={(comment) => void handleConfirm(comment)}
          busy={isBusy}
          errorMessage={actionError}
        />
      )}
    </div>
  )
}
