import { useCallback, useEffect, useState } from 'react'
import { getApprovalActions, PaymentApiError } from './api'
import type { ApprovalActionDto } from './types'

export type ApprovalActionsState = 'loading' | 'ready' | 'unavailable'

/**
 * Loads the approval-action history for `components/ApprovalChainBar.tsx` (S9-FE-02 DoD: "show
 * every step — role/approver/time/comment"). `getApprovalActions` calls the documented-but-not-yet-
 * implemented `GET .../approval-actions` endpoint (see `api.ts`'s remarks, L-04) — a 404 there is
 * expected on the real backend today, so it is treated as `'unavailable'`, not `'error'`: a genuine
 * network failure or an unexpected 5xx is logged distinctly (still resolves to `'unavailable'` for
 * rendering purposes — `ApprovalChainBar` has exactly one honest "history not available" state,
 * never a scary red error banner for a known, tracked gap) but the *reason* is kept so a future
 * fix can tell them apart if needed.
 */
export function useApprovalActions(paymentCertificateId: string | null) {
  const [actions, setActions] = useState<ApprovalActionDto[] | null>(null)
  const [state, setState] = useState<ApprovalActionsState>('loading')
  const [unavailableReason, setUnavailableReason] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!paymentCertificateId) {
      setActions(null)
      setState('unavailable')
      return
    }

    setState('loading')
    try {
      const data = await getApprovalActions(paymentCertificateId)
      setActions(data)
      setState('ready')
      setUnavailableReason(null)
    } catch (error) {
      setActions(null)
      setState('unavailable')
      setUnavailableReason(
        error instanceof PaymentApiError ? error.message : 'โหลดประวัติการอนุมัติโดยละเอียดไม่สำเร็จ',
      )
    }
  }, [paymentCertificateId])

  useEffect(() => {
    void load()
  }, [load])

  return { actions, state, unavailableReason, reload: load }
}
