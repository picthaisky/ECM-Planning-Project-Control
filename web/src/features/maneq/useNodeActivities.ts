import { useEffect, useState } from 'react'
import { listNodeActivities } from './api'
import type { ActivityOptionDto } from './types'

/**
 * Loads the activities under the currently-selected WBS node (`GET .../wbs-nodes/{nodeId}/activities`)
 * for the form's dependent activity dropdown. Returns an empty list when no node is selected or on any
 * failure — the form then falls back to a raw-GUID activity text input, so logging is never blocked.
 * Reloads whenever the selected node changes.
 */
export function useNodeActivities(projectId: string, wbsNodeId: string): ActivityOptionDto[] {
  const [activities, setActivities] = useState<ActivityOptionDto[]>([])

  useEffect(() => {
    if (!projectId || !wbsNodeId) {
      setActivities([])
      return
    }
    let cancelled = false
    listNodeActivities(projectId, wbsNodeId)
      .then((loaded) => {
        if (!cancelled) setActivities(loaded)
      })
      .catch(() => {
        if (!cancelled) setActivities([])
      })
    return () => {
      cancelled = true
    }
  }, [projectId, wbsNodeId])

  return activities
}
