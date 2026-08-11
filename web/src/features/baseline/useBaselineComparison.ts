import { useCallback, useEffect, useState } from 'react'
import { BASELINE_NO_ACTIVE_BASELINE_CODE, BaselineApiError, compareBaseline } from './api'
import type { BaselineComparisonDto } from './types'

export type BaselineComparisonLoadState = 'loading' | 'ready' | 'error' | 'no-active-baseline'

/**
 * S14-FE-01: loads the real, live `GET .../baselines/compare` (S14-BE-02) — the delta table +
 * summary tiles. Independent of `useBaselines`'s `listBaselines` 404 gap (see that hook's own
 * remarks): the backend defaults to the project's currently-active baseline when `baselineId` is
 * omitted, so this loads correctly even when the baseline *list* cannot be shown.
 *
 * `BaselineErrorCodes.NoActiveBaseline` (422) gets its own load state — a well-formed request the
 * project's current state cannot answer yet (no baseline ever captured/activated), distinct from a
 * genuine load failure, so `BaselinePage` can show "capture and activate a baseline first" instead
 * of a generic error banner.
 */
export function useBaselineComparison(projectId: string, baselineId?: string) {
  const [comparison, setComparison] = useState<BaselineComparisonDto | null>(null)
  const [loadState, setLoadState] = useState<BaselineComparisonLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await compareBaseline(projectId, { baselineId })
      setComparison(data)
      setLoadState('ready')
    } catch (error) {
      if (error instanceof BaselineApiError && error.code === BASELINE_NO_ACTIVE_BASELINE_CODE) {
        setLoadState('no-active-baseline')
        return
      }
      setLoadState('error')
      setLoadError(error instanceof BaselineApiError ? error.message : 'โหลดข้อมูลเปรียบเทียบ Baseline ไม่สำเร็จ')
    }
  }, [projectId, baselineId])

  useEffect(() => {
    void load()
  }, [load])

  return { comparison, loadState, loadError, reload: load }
}
