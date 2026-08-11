import { useCallback, useEffect, useState } from 'react'
import { IssueApiError, listIssues } from './api'
import type { IssueListResultDto } from './types'

export type IssuesLoadState = 'loading' | 'ready' | 'error'

const EMPTY_RESULT: IssueListResultDto = { items: [], totalCount: 0, statusCounts: { open: 0, doing: 0, closed: 0 } }

/**
 * Loads the S11-FE-01 issue register (`GET .../projects/{projectId}/issues` — real, live, one
 * response carrying both `items` and `statusCounts`) — mirrors
 * `features/vo/useVariationOrders.ts`'s established load/reload shape.
 *
 * Exposes `result` (the whole `IssueListResultDto`) rather than unpacking it into separate
 * `items`/`totalCount`/`statusCounts` state variables — this is deliberate: it makes it structurally
 * impossible for a caller to update one without the other (the exact "tile counts must match the
 * table" bug class the DoD calls out), since there is only ever one atomic value to read from.
 */
export function useIssues(projectId: string) {
  const [result, setResult] = useState<IssueListResultDto>(EMPTY_RESULT)
  const [loadState, setLoadState] = useState<IssuesLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await listIssues(projectId)
      setResult(data)
      setLoadState('ready')
      return data
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof IssueApiError ? error.message : 'โหลดรายการปัญหาไม่สำเร็จ')
      return null
    }
  }, [projectId])

  useEffect(() => {
    void reload()
  }, [reload])

  return { result, loadState, loadError, reload }
}
