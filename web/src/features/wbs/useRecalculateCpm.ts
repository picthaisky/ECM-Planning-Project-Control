import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { recalculateCpm, WbsApiError } from './api'
import type { RecalculateCpmResult } from './types'

export type RecalculateCpmState = 'idle' | 'calculating' | 'success' | 'error'

/**
 * Drives the S5-FE-01 "คำนวณ CPM ใหม่" action (US-5.1): calls the real
 * `POST /api/v1/projects/{projectId}/cpm/recalculate` (`RecalculateCpmCommand`, S5-BE-04) and
 * tracks a calculating/success/error status that `CriticalPathPreview` renders — never a silent
 * fire-and-forget network call (DoD: "แสดงสถานะกำลังคำนวณ/สำเร็จ/ล้มเหลว").
 *
 * On success this also raises the same bottom-right toast the working prototype's own "⟳ คำนวณ"
 * action shows (`doCalc`: `"คำนวณ CPM + EVM ใหม่แล้ว · 128 กิจกรรม · 0.4 วินาที"`) — the elapsed
 * time shown is this request's own round-trip (client-measured), not a server-reported figure,
 * since `RecalculateCpmResultDto` does not return one.
 */
export function useRecalculateCpm(projectId: string) {
  const [state, setState] = useState<RecalculateCpmState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<RecalculateCpmResult | null>(null)

  const recalculate = useCallback(async () => {
    setState('calculating')
    setError(null)
    const startedAt = Date.now()

    try {
      const response = await recalculateCpm(projectId)
      setResult(response)
      setState('success')

      const elapsedSeconds = (Date.now() - startedAt) / 1000
      pushToast({
        message:
          `คำนวณ CPM ใหม่สำเร็จ · ${response.activitiesProcessed.toLocaleString('th-TH')} กิจกรรม` +
          ` (วิกฤต ${response.criticalActivityCount.toLocaleString('th-TH')}) · ${elapsedSeconds.toFixed(2)} วินาที`,
      })
      return response
    } catch (err) {
      setState('error')
      setError(err instanceof WbsApiError ? err.message : 'คำนวณ CPM ใหม่ไม่สำเร็จ')
      return null
    }
  }, [projectId])

  return { state, error, result, recalculate }
}
