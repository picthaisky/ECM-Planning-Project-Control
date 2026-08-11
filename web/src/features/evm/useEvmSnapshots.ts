import { useCallback, useEffect, useState } from 'react'
import { EvmApiError, listEvmSnapshots } from './api'
import type { EvmSnapshotDto } from './types'

export type EvmSnapshotsLoadState = 'loading' | 'ready' | 'error'

/**
 * S7-FE-04: closed-period history (`GET .../evm/snapshots`, ADR-0009) for the S-Curve's historical
 * (pre-data-date) points. Deliberately loaded via its own independent hook rather than folded into
 * `useEvmData` — a snapshot-history failure degrades the chart to "just the live point" (see
 * `EvmPage.tsx`) instead of failing the whole EVM screen, since the 12-metric table and the EAC
 * variant selector don't need this data at all.
 */
export function useEvmSnapshots(projectId: string) {
  const [snapshots, setSnapshots] = useState<EvmSnapshotDto[]>([])
  const [loadState, setLoadState] = useState<EvmSnapshotsLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await listEvmSnapshots(projectId)
      setSnapshots(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof EvmApiError ? error.message : 'โหลดประวัติงวด EVM ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  return { snapshots, loadState, loadError, reload: load }
}
