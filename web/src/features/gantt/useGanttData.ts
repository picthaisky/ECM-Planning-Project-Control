import { useCallback, useEffect, useState } from 'react'
import { getGantt, GanttApiError } from './api'
import type { GanttActivityDto } from './types'

export type GanttLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the real Gantt read (`GET .../gantt`, S6-BE-01) for `GanttPage` — same
 * load/error-state shape as `useWbsTree.ts`, this feature's closest sibling.
 */
export function useGanttData(projectId: string) {
  const [activities, setActivities] = useState<GanttActivityDto[]>([])
  const [dataDateIso, setDataDateIso] = useState<string | null>(null)
  const [loadState, setLoadState] = useState<GanttLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const gantt = await getGantt(projectId)
      setActivities(gantt.activities)
      setDataDateIso(gantt.dataDate)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof GanttApiError ? error.message : 'โหลดข้อมูล Gantt ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  return { activities, dataDateIso, loadState, loadError, reload: load }
}
