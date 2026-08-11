import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import {
  approveVariationOrder,
  cancelVariationOrder,
  rejectVariationOrder,
  returnVariationOrderForRevision,
  submitVariationOrder,
  VoApiError,
  withdrawVariationOrder,
} from './api'
import type { VariationOrderDto } from './types'

export type VoActionKind = 'submit' | 'approve' | 'return' | 'reject' | 'withdraw' | 'cancel'

/**
 * Drives every S10-BE-01/02/03 workflow transition for one VO (submit/approve/return-for-revision/
 * reject/withdraw/cancel) — hand-rolled busy/error state, mirroring
 * `features/payment/usePaymentCertificateActions.ts`'s established pattern. `onUpdated` is called
 * with the fresh `VariationOrderDto` every real command already returns, so the caller
 * (`useVariationOrders.ts#applyUpdatedVo`) never needs a separate re-fetch.
 *
 * **Quorum-progress honesty, on BOTH approve and reject (ADR-0016's own point).** Reject is now
 * quorum-bound exactly like approve, so a `200 success=true` response to `reject` whose `status`/
 * `currentStepNo` are *unchanged* from before the call means the same thing a first approver's
 * unchanged response meant in Sprint 9: the server *did* record the vote (an `ApprovalAction` row
 * was written), it just did not terminate the document because `QuorumCount` was not yet satisfied.
 * Both `approve` and `reject` below diff the VO immediately before and immediately after the call
 * and expose `quorumPendingNotice: true` (with an action-appropriate `quorumPendingMessage`) exactly
 * in that case — critically, the reject-flavoured message says the document is **NOT** rejected yet,
 * which is the specific thing ADR-0016 says the UI must never get wrong ("a single reject on a
 * 2-of-2 step no longer kills the document — the UI must not tell the user it did"). The *exact*
 * remaining vote count is derived separately, when derivable, by `voteProgress.ts` from real
 * approval-action history — this diff is the fallback for when that history is not available
 * (realistically, always, today — see `api.ts#getVoApprovalActions`'s remarks).
 */
export function useVariationOrderActions(
  variationOrder: VariationOrderDto | null,
  onUpdated: (updated: VariationOrderDto) => void,
) {
  const [busyAction, setBusyAction] = useState<VoActionKind | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [quorumPendingNotice, setQuorumPendingNotice] = useState(false)
  const [quorumPendingMessage, setQuorumPendingMessage] = useState<string | null>(null)
  /** Which vote caused the current `quorumPendingNotice` — lets the caller (`VoPage.tsx`) pick the
   * right verb ("Approve"/"Reject") when it augments this generic message with a real vote count
   * from `voteProgress.ts#computeStepVoteProgress`, once history is available. `null` whenever
   * `quorumPendingNotice` is false. */
  const [quorumPendingAction, setQuorumPendingAction] = useState<'approve' | 'reject' | null>(null)

  const resetTransientState = useCallback(() => {
    setActionError(null)
    setQuorumPendingNotice(false)
    setQuorumPendingMessage(null)
    setQuorumPendingAction(null)
  }, [])

  const submit = useCallback(async () => {
    if (!variationOrder) return null
    setBusyAction('submit')
    resetTransientState()
    try {
      const updated = await submitVariationOrder(variationOrder.id)
      onUpdated(updated)
      pushToast({
        message:
          updated.totalSteps > 0
            ? `ส่งขออนุมัติ ${updated.voNumber} เรียบร้อยแล้ว (ขั้นตอนที่ ${updated.currentStepNo}/${updated.totalSteps})`
            : `ส่งขออนุมัติ ${updated.voNumber} เรียบร้อยแล้ว`,
      })
      return updated
    } catch (error) {
      setActionError(error instanceof VoApiError ? error.message : 'ส่งขออนุมัติไม่สำเร็จ')
      return null
    } finally {
      setBusyAction(null)
    }
  }, [variationOrder, onUpdated, resetTransientState])

  const approve = useCallback(
    async (comment?: string) => {
      if (!variationOrder) return null
      const before = variationOrder
      setBusyAction('approve')
      resetTransientState()
      try {
        const updated = await approveVariationOrder(variationOrder.id, comment)
        onUpdated(updated)

        const quorumStillPending = updated.status === before.status && updated.currentStepNo === before.currentStepNo
        if (quorumStillPending) {
          setQuorumPendingNotice(true)
          setQuorumPendingAction('approve')
          setQuorumPendingMessage(
            `บันทึกการอนุมัติของคุณในขั้นตอนที่ ${updated.currentStepNo} แล้ว แต่ขั้นตอนนี้ยังต้องการผู้อนุมัติเพิ่มเติม (Quorum ยังไม่ครบ) — เอกสารยังคงเป็น "รออนุมัติ"`,
          )
          pushToast({
            message: `บันทึกการอนุมัติของคุณในขั้นตอนที่ ${updated.currentStepNo} แล้ว แต่ขั้นตอนนี้ยังต้องการผู้อนุมัติเพิ่มเติม (Quorum ยังไม่ครบ)`,
          })
        } else if (updated.status === 'Approved') {
          pushToast({ message: `อนุมัติครบทุกขั้นตอนแล้ว ${updated.voNumber} ถูกอนุมัติ และปรับ BAC/มูลค่าสัญญาแล้ว` })
        } else {
          pushToast({
            message: `อนุมัติขั้นตอนที่ ${before.currentStepNo} เรียบร้อยแล้ว ไปขั้นตอนที่ ${updated.currentStepNo}/${updated.totalSteps}`,
          })
        }
        return updated
      } catch (error) {
        setActionError(error instanceof VoApiError ? error.message : 'อนุมัติไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [variationOrder, onUpdated, resetTransientState],
  )

  const returnForRevision = useCallback(
    async (comment: string) => {
      if (!variationOrder) return null
      setBusyAction('return')
      resetTransientState()
      try {
        const updated = await returnVariationOrderForRevision(variationOrder.id, comment)
        onUpdated(updated)
        pushToast({
          message: `ตีกลับ ${updated.voNumber} เพื่อแก้ไขแล้ว — กลับเป็นฉบับร่าง (revision ${updated.revisionNo}) แก้ไขแล้วส่งใหม่ได้`,
        })
        return updated
      } catch (error) {
        setActionError(error instanceof VoApiError ? error.message : 'ตีกลับเอกสารไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [variationOrder, onUpdated, resetTransientState],
  )

  const reject = useCallback(
    async (comment: string) => {
      if (!variationOrder) return null
      const before = variationOrder
      setBusyAction('reject')
      resetTransientState()
      try {
        const updated = await rejectVariationOrder(variationOrder.id, comment)
        onUpdated(updated)

        // ADR-0016: reject is quorum-bound exactly like approve — an unchanged status/step means
        // the rejection vote was recorded but did NOT terminate the document. Telling the user
        // otherwise is the exact regression this ADR exists to prevent.
        const quorumStillPending = updated.status === before.status && updated.currentStepNo === before.currentStepNo
        if (quorumStillPending) {
          setQuorumPendingNotice(true)
          setQuorumPendingAction('reject')
          setQuorumPendingMessage(
            'บันทึกการปฏิเสธของคุณแล้ว แต่ขั้นตอนนี้ยังต้องการผู้ปฏิเสธเพิ่มเติมให้ครบ Quorum — เอกสารฉบับนี้ยังไม่ถูกปฏิเสธ และยังคงเป็น "รออนุมัติ"',
          )
          pushToast({
            message: 'บันทึกการปฏิเสธของคุณแล้ว แต่เอกสารยังไม่ถูกปฏิเสธ — ยังต้องการผู้ปฏิเสธเพิ่มเติมให้ครบ Quorum',
          })
        } else if (updated.status === 'Rejected') {
          pushToast({ message: `ปฏิเสธ ${updated.voNumber} แล้ว — สถานะนี้เป็นการยุติถาวรและไม่สามารถย้อนกลับได้` })
        }
        return updated
      } catch (error) {
        setActionError(error instanceof VoApiError ? error.message : 'ปฏิเสธเอกสารไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [variationOrder, onUpdated, resetTransientState],
  )

  const withdraw = useCallback(async () => {
    if (!variationOrder) return null
    setBusyAction('withdraw')
    resetTransientState()
    try {
      const updated = await withdrawVariationOrder(variationOrder.id)
      onUpdated(updated)
      pushToast({ message: `ถอนคำขออนุมัติ ${updated.voNumber} แล้ว — กลับเป็นฉบับร่าง` })
      return updated
    } catch (error) {
      setActionError(error instanceof VoApiError ? error.message : 'ถอนคำขอไม่สำเร็จ')
      return null
    } finally {
      setBusyAction(null)
    }
  }, [variationOrder, onUpdated, resetTransientState])

  const cancel = useCallback(
    async (comment: string) => {
      if (!variationOrder) return null
      setBusyAction('cancel')
      resetTransientState()
      try {
        const updated = await cancelVariationOrder(variationOrder.id, comment)
        onUpdated(updated)
        pushToast({ message: `ยกเลิก ${updated.voNumber} แล้ว` })
        return updated
      } catch (error) {
        setActionError(error instanceof VoApiError ? error.message : 'ยกเลิกเอกสารไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [variationOrder, onUpdated, resetTransientState],
  )

  return {
    busyAction,
    actionError,
    clearActionError: useCallback(() => setActionError(null), []),
    quorumPendingNotice,
    quorumPendingMessage,
    quorumPendingAction,
    submit,
    approve,
    returnForRevision,
    reject,
    withdraw,
    cancel,
  }
}
