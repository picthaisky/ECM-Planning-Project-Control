import { useCallback, useState } from 'react'
import { getNodeActivities, WbsApiError } from './api'
import type { ActivityForProgress } from './types'

export type NodeActivitiesState = 'idle' | 'loading' | 'ready' | 'error'

/**
 * Loads the activities under a chosen WBS node for the "โหมดอัปเดตความคืบหน้า" grid from the live
 * `getNodeActivities` endpoint (`api.ts`). `state` reaching `'error'` (e.g. a genuine network/404
 * failure) is a correctly-handled outcome, not a bug in this hook — `ProgressUpdatePanel` still
 * offers the manual add-row path either way.
 */
export function useNodeActivities() {
  const [state, setState] = useState<NodeActivitiesState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [activities, setActivities] = useState<ActivityForProgress[]>([])

  const loadForNode = useCallback(async (projectId: string, wbsNodeId: string) => {
    setState('loading')
    setError(null)
    try {
      const result = await getNodeActivities(projectId, wbsNodeId)
      setActivities(result)
      setState('ready')
      return result
    } catch (err) {
      setState('error')
      setError(err instanceof WbsApiError ? err.message : 'โหลดรายการกิจกรรมไม่สำเร็จ')
      return []
    }
  }, [])

  return { state, error, activities, loadForNode }
}
