import { useCallback, useEffect, useState } from 'react'
import { EvmApiError, getEvm } from './api'
import type { EvmResponseDto } from './types'

export type EvmLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the real EVM read (`GET .../evm`, S7-BE-03) for `EvmPage` — same load/error-state shape as
 * `features/gantt/useGanttData.ts`/`features/wbs/useWbsTree.ts`, this feature's closest siblings.
 * Never passes a `dataDate`/`eacVariant` override (see `api.ts#getEvm`'s remarks) — one fetch per
 * mount is the whole point of "no round trip" (design.md §1.1).
 */
export function useEvmData(projectId: string) {
  const [evm, setEvm] = useState<EvmResponseDto | null>(null)
  const [loadState, setLoadState] = useState<EvmLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getEvm(projectId)
      setEvm(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof EvmApiError ? error.message : 'โหลดข้อมูล EVM ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  return { evm, loadState, loadError, reload: load }
}
