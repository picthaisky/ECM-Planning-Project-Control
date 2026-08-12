import { useCallback, useState } from 'react'
import { simulateApprovalRouting, TenantAdminApiError } from './api'
import type { ApprovalDocumentType, ApprovalRoutingSimulation, SimulateApprovalRoutingPayload } from './types'

export type RoutingSimulationState = 'idle' | 'simulating' | 'error'

/**
 * Drives the S15-BE-01 "ทดสอบเส้นทางอนุมัติ" action (`POST .../{documentType}/simulate`) — a trial
 * run against the real resolution path, without creating any document. Deliberately not auto-run on
 * mount/tab-switch (unlike `useApprovalPolicy.ts`/`useApprovalPolicyHistory.ts`'s `GET`s): this is a
 * `POST`-shaped action the Admin triggers explicitly with a hypothetical project/amount, per the
 * DoD's "ทดลอง routing ได้".
 */
export function useRoutingSimulator(tenantId: string, documentType: ApprovalDocumentType) {
  const [state, setState] = useState<RoutingSimulationState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ApprovalRoutingSimulation | null>(null)

  const simulate = useCallback(
    async (payload: SimulateApprovalRoutingPayload): Promise<ApprovalRoutingSimulation | null> => {
      setState('simulating')
      setError(null)
      setResult(null)
      try {
        const data = await simulateApprovalRouting(tenantId, documentType, payload)
        setResult(data)
        setState('idle')
        return data
      } catch (err) {
        setState('error')
        setError(err instanceof TenantAdminApiError ? err.message : 'จำลองเส้นทางอนุมัติไม่สำเร็จ')
        return null
      }
    },
    [tenantId, documentType],
  )

  const reset = useCallback(() => {
    setResult(null)
    setError(null)
    setState('idle')
  }, [])

  return { simulate, state, error, result, reset }
}
