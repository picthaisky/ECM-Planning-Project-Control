import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import {
  approvePaymentCertificate,
  PaymentApiError,
  recordPayment,
  rejectPaymentCertificate,
  returnPaymentCertificateForRevision,
  submitPaymentCertificate,
} from './api'
import type { PaymentCertificateDto } from './types'

export type CertificateActionKind = 'submit' | 'approve' | 'return' | 'reject' | 'record-payment'

/**
 * Drives every S9-BE-05 workflow transition for one certificate (submit/approve/return-for-
 * revision/reject/record-payment) — hand-rolled busy/error state, matching
 * `features/wbs/useRecalculateCpm.ts`'s established pattern. `onUpdated` is called with the fresh
 * `PaymentCertificateDto` every real command already returns, so the caller
 * (`usePaymentCertificates.ts#applyUpdatedCertificate`) never needs a separate re-fetch.
 *
 * **Quorum-progress honesty (security review sprint-09.md §9.5(ii), informational).**
 * `PaymentCertificateDto` exposes neither `ApprovalSteps` nor a quorum-progress count, so a `200
 * success=true` response to `approve` whose `status`/`currentStepNo` are *unchanged* from before the
 * call is indistinguishable, at the wire level, from a no-op — except that it is not one: the
 * server *did* record the vote (`ApprovalAction` row written, `H-02` fix), it just did not clear the
 * step because `QuorumCount` was not yet satisfied. `approve` below diffs the certificate
 * immediately before and immediately after the call and exposes `quorumPendingNotice: true` exactly
 * in that case, so the UI can render an honest "your approval was recorded; the step still needs
 * more signatures" state instead of either a misleading generic-success toast or (worse) silence.
 * The *exact* remaining count (N more required) is genuinely not derivable from this contract — the
 * DTO has no quorum target to compare against — and is reported as a backend gap rather than guessed
 * (see this sprint's frontend report).
 */
export function usePaymentCertificateActions(
  certificate: PaymentCertificateDto | null,
  onUpdated: (updated: PaymentCertificateDto) => void,
) {
  const [busyAction, setBusyAction] = useState<CertificateActionKind | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [quorumPendingNotice, setQuorumPendingNotice] = useState(false)

  const submit = useCallback(async () => {
    if (!certificate) return null
    setBusyAction('submit')
    setActionError(null)
    setQuorumPendingNotice(false)
    try {
      const updated = await submitPaymentCertificate(certificate.id)
      onUpdated(updated)
      pushToast({
        message:
          updated.totalSteps > 0
            ? `ส่งขออนุมัติใบรับรองผลงานเรียบร้อยแล้ว (ขั้นตอนที่ ${updated.currentStepNo}/${updated.totalSteps})`
            : 'ส่งขออนุมัติใบรับรองผลงานเรียบร้อยแล้ว',
      })
      return updated
    } catch (error) {
      setActionError(error instanceof PaymentApiError ? error.message : 'ส่งขออนุมัติไม่สำเร็จ')
      return null
    } finally {
      setBusyAction(null)
    }
  }, [certificate, onUpdated])

  const approve = useCallback(
    async (comment?: string) => {
      if (!certificate) return null
      const before = certificate
      setBusyAction('approve')
      setActionError(null)
      setQuorumPendingNotice(false)
      try {
        const updated = await approvePaymentCertificate(certificate.id, comment)
        onUpdated(updated)

        const quorumStillPending = updated.status === before.status && updated.currentStepNo === before.currentStepNo
        if (quorumStillPending) {
          setQuorumPendingNotice(true)
          pushToast({
            message: `บันทึกการอนุมัติของคุณในขั้นตอนที่ ${updated.currentStepNo} แล้ว แต่ขั้นตอนนี้ยังต้องการผู้อนุมัติเพิ่มเติม (Quorum ยังไม่ครบ)`,
          })
        } else if (updated.status === 'Certified') {
          pushToast({ message: 'อนุมัติครบทุกขั้นตอนแล้ว ใบรับรองผลงานถูกตรวจรับ (Certified)' })
        } else {
          pushToast({
            message: `อนุมัติขั้นตอนที่ ${before.currentStepNo} เรียบร้อยแล้ว ไปขั้นตอนที่ ${updated.currentStepNo}/${updated.totalSteps}`,
          })
        }
        return updated
      } catch (error) {
        setActionError(error instanceof PaymentApiError ? error.message : 'อนุมัติไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [certificate, onUpdated],
  )

  const returnForRevision = useCallback(
    async (comment: string) => {
      if (!certificate) return null
      setBusyAction('return')
      setActionError(null)
      setQuorumPendingNotice(false)
      try {
        const updated = await returnPaymentCertificateForRevision(certificate.id, comment)
        onUpdated(updated)
        pushToast({ message: `ตีกลับเอกสารเพื่อแก้ไขแล้ว — กลับเป็นฉบับร่าง (revision ${updated.revisionNo})` })
        return updated
      } catch (error) {
        setActionError(error instanceof PaymentApiError ? error.message : 'ตีกลับเอกสารไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [certificate, onUpdated],
  )

  const reject = useCallback(
    async (comment: string) => {
      if (!certificate) return null
      setBusyAction('reject')
      setActionError(null)
      setQuorumPendingNotice(false)
      try {
        const updated = await rejectPaymentCertificate(certificate.id, comment)
        onUpdated(updated)
        pushToast({ message: 'ปฏิเสธเอกสารแล้ว — สถานะนี้เป็นการยุติถาวรและไม่สามารถย้อนกลับได้' })
        return updated
      } catch (error) {
        setActionError(error instanceof PaymentApiError ? error.message : 'ปฏิเสธเอกสารไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [certificate, onUpdated],
  )

  const recordCertificatePayment = useCallback(
    async (reference: string, paidAt: string) => {
      if (!certificate) return null
      setBusyAction('record-payment')
      setActionError(null)
      setQuorumPendingNotice(false)
      try {
        const updated = await recordPayment(certificate.id, reference, paidAt)
        onUpdated(updated)
        pushToast({ message: `บันทึกการจ่ายเงินแล้ว (เลขที่อ้างอิง ${reference})` })
        return updated
      } catch (error) {
        setActionError(error instanceof PaymentApiError ? error.message : 'บันทึกการจ่ายเงินไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [certificate, onUpdated],
  )

  return {
    busyAction,
    actionError,
    clearActionError: useCallback(() => setActionError(null), []),
    quorumPendingNotice,
    submit,
    approve,
    returnForRevision,
    reject,
    recordPayment: recordCertificatePayment,
  }
}
