import { useCallback, useEffect, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { activateBaseline, BaselineApiError, captureBaseline, listBaselines } from './api'
import type { BaselineDto } from './types'

export type BaselinesLoadState = 'loading' | 'ready'
export type BaselineActionState = 'idle' | 'busy' | 'error'

/**
 * S14-FE-01: loads and manages the Baseline screen's "จัดการหลายชุด" list.
 *
 * **Handles `listBaselines`'s known 404 gap by design, not by accident** (see `api.ts#listBaselines`'s
 * own remarks): a failed list load never puts this hook into a blocking `error` state — there is
 * genuinely no such state (`BaselinesLoadState` only has `loading`/`ready`) — it instead falls back
 * to an empty, **session-local** list (`listAvailable: false`), which `capture`/`activate` below
 * then populate/update from their own real, live responses for the rest of this browser session.
 * `BaselineListPanel.tsx` shows a clear, honest inline note whenever `listAvailable` is `false`,
 * rather than silently pretending the list is complete.
 */
export function useBaselines(projectId: string) {
  const [baselines, setBaselines] = useState<BaselineDto[]>([])
  const [loadState, setLoadState] = useState<BaselinesLoadState>('loading')
  const [listAvailable, setListAvailable] = useState(true)
  const [actionState, setActionState] = useState<BaselineActionState>('idle')
  const [actionError, setActionError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    try {
      const data = await listBaselines(projectId)
      setBaselines(data)
      setListAvailable(true)
    } catch {
      // Deliberately silent here — see this hook's own doc comment. `listBaselines`'s real 404
      // today is an *expected*, already-documented gap, not a surprising failure to alarm the user
      // about on every page load; `BaselineListPanel` is what actually tells them.
      setBaselines([])
      setListAvailable(false)
    } finally {
      setLoadState('ready')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  const capture = useCallback(
    async (name: string): Promise<BaselineDto | null> => {
      setActionState('busy')
      setActionError(null)
      try {
        const created = await captureBaseline(projectId, name)
        // Prepend regardless of `listAvailable` — even once the real list endpoint exists, showing
        // the just-created row immediately (rather than waiting for a re-fetch) is the same
        // "the mutation's own response is already the freshest source" discipline
        // `features/vo/useVariationOrders.ts#applyUpdatedVo` already establishes.
        setBaselines((current) => [created, ...current])
        setActionState('idle')
        pushToast({ message: `บันทึก Baseline "${created.name}" แล้ว` })
        return created
      } catch (error) {
        setActionState('error')
        setActionError(error instanceof BaselineApiError ? error.message : 'บันทึก Baseline ไม่สำเร็จ')
        return null
      }
    },
    [projectId],
  )

  const activate = useCallback(
    async (baselineId: string): Promise<boolean> => {
      setActionState('busy')
      setActionError(null)
      try {
        const result = await activateBaseline(projectId, baselineId)
        // Activation is exclusive (DB-enforced unique-active-per-project) — flip every row's
        // `isActive` locally rather than only the target, so the list never shows two "active"
        // baselines even transiently. Only `isActive` is trusted from this narrower response
        // (`ActivateBaselineResultDto` carries no other field) — every other column keeps its
        // already-known value.
        setBaselines((current) =>
          current.map((b) => (b.id === result.id ? { ...b, isActive: result.isActive } : { ...b, isActive: false })),
        )
        setActionState('idle')
        pushToast({ message: 'เปิดใช้งาน Baseline นี้แล้ว' })
        return true
      } catch (error) {
        setActionState('error')
        setActionError(error instanceof BaselineApiError ? error.message : 'เปิดใช้งาน Baseline ไม่สำเร็จ')
        return false
      }
    },
    [projectId],
  )

  const clearActionError = useCallback(() => setActionError(null), [])

  return {
    baselines,
    loadState,
    listAvailable,
    reload: load,
    capture,
    activate,
    actionState,
    actionError,
    clearActionError,
  }
}
