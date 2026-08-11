import { useCallback, useEffect, useState } from 'react'
import { CashFlowApiError, getCashFlow } from './api'
import type { CashFlowResponseDto } from './types'

export type CashFlowLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the real Cash Flow read (`GET .../cash-flow`, S8-BE-01) for `CashFlowPage` — same
 * load/error-state shape as `features/evm/useEvmData.ts`/`features/dashboard/useDashboardData.ts`.
 * Only mounted inside `RequireRole` (see `CashFlowPage.tsx`), so this never fires for a role the
 * server would reject anyway. No `dataDate`/`from` override passed — no date/range picker exists in
 * this sprint's UI (mirrors `useDashboardData`/`useEvmData`'s identical "no round trip needed" note).
 */
export function useCashFlowData(projectId: string) {
  const [cashFlow, setCashFlow] = useState<CashFlowResponseDto | null>(null)
  const [loadState, setLoadState] = useState<CashFlowLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getCashFlow(projectId)
      setCashFlow(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof CashFlowApiError ? error.message : 'โหลดข้อมูล Cash Flow ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  return { cashFlow, loadState, loadError, reload: load }
}
