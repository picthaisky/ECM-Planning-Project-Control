import { useCallback, useState } from 'react'
import { getNodeActivities, WbsApiError } from './api'
import type { ActivityForProgress } from './types'

export type NodeActivitiesState = 'idle' | 'loading' | 'ready' | 'error'

/**
 * Loads the activities under a chosen WBS node for the "โหมดอัปเดตความคืบหน้า" grid. See
 * `api.ts`'s `getNodeActivities` remarks: this read endpoint does not exist on the real backend
 * yet, so `state` reaching `'error'` (a 404 today) is an expected, correctly-handled outcome, not
 * a bug in this hook — `ProgressUpdatePanel` still offers the manual add-row path either way.
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
