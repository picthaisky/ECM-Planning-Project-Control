import { useState } from 'react'
import { Button, StatusPill } from '../../../components'
import { cx } from '../../../utils/cx'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import { ApprovalActionModal } from './ApprovalActionModal'
import type { ApprovalActionModalKind } from './ApprovalActionModal'
import type { ApprovalActionsState } from '../useApprovalActions'
import type { ChainStepTone } from '../chainPermissions'
import type { ApprovalActionDto } from '../types'

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

const DEFAULT_TITLE = 'เส้นทางการอนุมัติ (Approval Chain)'
const DEFAULT_NOT_SUBMITTED_MESSAGE = 'ยังไม่ได้ส่งขออนุมัติเอกสารฉบับนี้'
const DEFAULT_APPROVE_LABEL = 'อนุมัติ'
const DEFAULT_RETURN_LABEL = 'ตีกลับแก้ไข'
const DEFAULT_REJECT_LABEL = 'ปฏิเสธ'

export interface ApprovalChainBarProps {
  /** Heading text — defaults to 'เส้นทางการอนุมัติ (Approval Chain)'. */
  title?: string
  /** Thai display text for the document's current status (e.g. `PAYMENT_STATUS_LABELS[status]` /
   * `VO_STATUS_LABELS[status]`) — this component renders exactly what it is given and no longer
   * owns a document-type-specific label table. */
  statusLabel: string
  /** Raw status keyword, passed through to `StatusPill` for tone resolution
   * (`components/statusTone.ts`). */
  statusValue: string
  totalSteps: number
  currentStepNo: number
  /** True before the document's first `Submit` (e.g. Payment's `NotDue`/`Draft`, VO's `Draft`) —
   * the caller decides which of its own statuses this means, since the two document types' state
   * machines differ. */
  notSubmittedYet: boolean
  notSubmittedMessage?: string
  /** Visual tone for step `stepNo` (1-based) — the caller supplies this (typically
   * `chainPermissions.ts#resolveChainStepTone` bound to its own document), since "done"/"rejected"
   * depend on a status vocabulary this component no longer needs to know. */
  stepTone: (stepNo: number) => ChainStepTone
  history: ApprovalActionDto[] | null
  historyState: ApprovalActionsState
  historyUnavailableReason: string | null
  quorumPendingNotice: boolean
  /** Full banner text for `quorumPendingNotice`. Defaults to the Payment Certificate wording built
   * from `currentStepNo`/`statusLabel` — pass an explicit message for a different action's quorum
   * (e.g. VO's reject-quorum notice, ADR-0016: "your rejection was recorded, but the document has
   * NOT been rejected yet — more rejectors are needed"), which is worded differently from an
   * approval quorum notice and must not reuse the approve-flavoured default silently. */
  quorumPendingMessage?: string
  /** Optional explanatory note rendered under the step row — e.g. why an escalation step (VO
   * cumulative-VO-escalation, domain-rules.md §4) is present in this chain. Omitted entirely when
   * `null`/`undefined`, never rendered as an empty box. */
  escalationNote?: string | null
  /** Whether to render each action button — precomputed by the caller from its own
   * `chainPermissions.ts` (role/self-approval/final-step rules differ per document type) plus
   * `currentUserId`. This component never guesses eligibility itself; the server remains the actual
   * authorization boundary in every case regardless of what renders here. */
  canApprove: boolean
  canReturnForRevision: boolean
  canReject: boolean
  approveLabel?: string
  returnForRevisionLabel?: string
  rejectLabel?: string
  busy: boolean
  actionError: string | null
  onApprove: (comment?: string) => Promise<unknown>
  onReturnForRevision: (comment: string) => Promise<unknown>
  onReject: (comment: string) => Promise<unknown>
}

/**
 * The approval-chain status bar (originally S9-FE-02, generalized for S10-FE-01 reuse across
 * document types per that task's explicit DoD: "reuse component ของ S9-FE-02"). Shows every step
 * position, the current step, and the append-only history (role/approver/time/comment) when it can
 * be loaded. Buttons render only for actions the caller has determined the current user might
 * plausibly be entitled to — the server remains the actual authorization boundary in every case.
 *
 * Document-type-agnostic by construction: every field this component previously read directly off
 * `PaymentCertificateDto` (status label/tone, "not submitted yet", per-step tone, who may act) is
 * now a prop, computed by the caller from its own DTO and its own `chainPermissions.ts` module —
 * `features/payment/PaymentPage.tsx` and `features/vo/VoPage.tsx` are the two call sites. This is
 * the same shape split `ApprovalActionModal` already uses (one shared presentational component,
 * per-kind configuration supplied by the caller), just at the level of a whole panel instead of a
 * dialog.
 *
 * "ตีกลับ" (`ReturnForRevision`) and "ปฏิเสธ" (`Reject`) are two distinct, clearly-labelled buttons
 * that open two distinctly-worded confirmation modals (`ApprovalActionModal`) — never conflated, the
 * exact mistake the prototype's own "ตีกลับ" badge made by mapping to `Rejected` (repeated for the
 * Sprint 10 VO screen's own prototype table — see `features/vo/voStatusLabels.ts`'s remarks).
 */
export function ApprovalChainBar({
  title = DEFAULT_TITLE,
  statusLabel,
  statusValue,
  totalSteps,
  currentStepNo,
  notSubmittedYet,
  notSubmittedMessage = DEFAULT_NOT_SUBMITTED_MESSAGE,
  stepTone,
  history,
  historyState,
  historyUnavailableReason,
  quorumPendingNotice,
  quorumPendingMessage,
  escalationNote,
  canApprove,
  canReturnForRevision,
  canReject,
  approveLabel = DEFAULT_APPROVE_LABEL,
  returnForRevisionLabel = DEFAULT_RETURN_LABEL,
  rejectLabel = DEFAULT_REJECT_LABEL,
  busy,
  actionError,
  onApprove,
  onReturnForRevision,
  onReject,
}: ApprovalChainBarProps) {
  const [openModal, setOpenModal] = useState<ApprovalActionModalKind | null>(null)

  const resolvedQuorumMessage =
    quorumPendingMessage ??
    `บันทึกการอนุมัติของคุณแล้ว แต่ขั้นตอนที่ ${currentStepNo} ยังต้องการผู้อนุมัติเพิ่มเติมให้ครบตาม Quorum ที่กำหนดไว้ — สถานะเอกสารยังคงเป็น "${statusLabel}"`

  async function handleConfirm(comment: string) {
    const action =
      openModal === 'approve' ? onApprove : openModal === 'return' ? onReturnForRevision : onReject
    const result = await action(comment)
    if (result) setOpenModal(null) // keep the modal open (with the error shown) on failure
  }

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-heading text-sm font-semibold text-navy">{title}</h3>
        <StatusPill label={statusLabel} status={statusValue} />
      </div>

      {notSubmittedYet ? (
        <p className="mt-3 text-xs text-text-faint">{notSubmittedMessage}</p>
      ) : (
        <>
          <div className="mt-3 flex items-center gap-1.5" role="list" aria-label="ขั้นตอนการอนุมัติ">
            {Array.from({ length: totalSteps }, (_, i) => i + 1).map((stepNo) => {
              const tone = stepTone(stepNo)
              return (
                <div key={stepNo} role="listitem" className="flex items-center gap-1.5">
                  <span
                    aria-label={`ขั้นตอนที่ ${stepNo}${stepNo === currentStepNo ? ' (ปัจจุบัน)' : ''}`}
                    className={cx(
                      'grid h-7 w-7 flex-none place-items-center rounded-full border text-[11px] font-semibold',
                      STEP_TONE_CLASS[tone],
                    )}
                  >
                    {stepNo}
                  </span>
                  {stepNo < totalSteps && <span aria-hidden="true" className="h-px w-6 bg-border" />}
                </div>
              )
            })}
            <span className="ml-2 text-[11px] text-text-faint">
              ขั้นตอนที่ {currentStepNo} จาก {totalSteps}
            </span>
          </div>

          {escalationNote && (
            <p className="mt-3 rounded border border-dashed border-border px-3 py-2 text-[11px] text-text-muted">
              {escalationNote}
            </p>
          )}

          {quorumPendingNotice && (
            <p role="status" className="mt-3 rounded border border-warning-text/30 bg-warning-text/10 px-3 py-2 text-[11px] text-warning-text">
              {resolvedQuorumMessage}
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
            {canApprove && (
              <Button size="sm" onClick={() => setOpenModal('approve')} disabled={busy}>
                {approveLabel}
              </Button>
            )}
            {canReturnForRevision && (
              <Button size="sm" variant="secondary" onClick={() => setOpenModal('return')} disabled={busy}>
                {returnForRevisionLabel}
              </Button>
            )}
            {canReject && (
              <Button size="sm" variant="danger" onClick={() => setOpenModal('reject')} disabled={busy}>
                {rejectLabel}
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
          busy={busy}
          errorMessage={actionError}
        />
      )}
    </div>
  )
}
