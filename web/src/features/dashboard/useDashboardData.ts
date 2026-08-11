import { useCallback, useEffect, useState } from 'react'
import { DashboardApiError, getDashboard } from './api'
import type { DashboardResponseDto } from './types'

export type DashboardLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the real Dashboard read (`GET .../dashboard`, S8-BE-02) for `DashboardPage` — same
 * load/error-state shape as `features/evm/useEvmData.ts`, this feature's closest sibling. Only
 * mounted inside `RequireRole` (see `DashboardPage.tsx`), so this never fires for a role the server
 * would reject anyway.
 */
export function useDashboardData(projectId: string) {
  const [dashboard, setDashboard] = useState<DashboardResponseDto | null>(null)
  const [loadState, setLoadState] = useState<DashboardLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getDashboard(projectId)
      setDashboard(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof DashboardApiError ? error.message : 'โหลดข้อมูล Dashboard ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  return { dashboard, loadState, loadError, reload: load }
}
