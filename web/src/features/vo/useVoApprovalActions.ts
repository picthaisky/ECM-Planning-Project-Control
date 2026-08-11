import { useCallback, useEffect, useState } from 'react'
import { getVoApprovalActions, VoApiError } from './api'
import type { ApprovalActionDto } from './types'

export type ApprovalActionsState = 'loading' | 'ready' | 'unavailable'

/**
 * Loads the approval-action history for `ApprovalChainBar`/`voteProgress.ts` — the VO analogue of
 * `features/payment/useApprovalActions.ts`. `getVoApprovalActions` calls a real, live-*routed*
 * endpoint whose query handler nonetheless 404s unconditionally today (a genuine backend gap, not
 * the "not implemented at all" gap Payment has — see `api.ts#getVoApprovalActions`'s remarks), so a
 * 404 here is treated as `'unavailable'`, not an `'error'`: this component has exactly one honest
 * "history not available" state, never a scary red banner for a known, tracked gap — but the reason
 * is kept so a future fix can tell a genuine failure apart if needed.
 */
export function useVoApprovalActions(variationOrderId: string | null) {
  const [actions, setActions] = useState<ApprovalActionDto[] | null>(null)
  const [state, setState] = useState<ApprovalActionsState>('loading')
  const [unavailableReason, setUnavailableReason] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!variationOrderId) {
      setActions(null)
      setState('unavailable')
      return
    }

    setState('loading')
    try {
      const data = await getVoApprovalActions(variationOrderId)
      setActions(data)
      setState('ready')
      setUnavailableReason(null)
    } catch (error) {
      setActions(null)
      setState('unavailable')
      setUnavailableReason(error instanceof VoApiError ? error.message : 'โหลดประวัติการอนุมัติโดยละเอียดไม่สำเร็จ')
    }
  }, [variationOrderId])

  useEffect(() => {
    void load()
  }, [load])

  return { actions, state, unavailableReason, reload: load }
}
