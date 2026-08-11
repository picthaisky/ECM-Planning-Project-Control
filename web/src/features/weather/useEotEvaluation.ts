import { useCallback, useState } from 'react'
import { evaluateEot, WeatherApiError } from './api'
import type { EotEvaluationDto, EvaluateEotPayload } from './types'

export type EotEvaluationState = 'idle' | 'evaluating' | 'success' | 'error'

/**
 * Drives the S11-BE-02 "ประเมิน EOT" action: calls the real
 * `POST /api/v1/projects/{projectId}/eot-evaluations` and tracks an evaluating/success/error status
 * — mirrors `features/wbs/useRecalculateCpm.ts`'s established shape for an analogous "run a real
 * calculation and show its own status" action.
 *
 * **Deliberately never auto-runs.** Every call is a genuine `POST` that persists a new, permanent
 * `EotEvaluation` row plus an audit-log entry (domain-rules.md §8.5) — triggering it on mount or on
 * every keystroke would silently write rows nobody asked for. It is wired to an explicit button
 * click only (`EotEvaluationPanel.tsx`).
 */
export function useEotEvaluation(projectId: string) {
  const [state, setState] = useState<EotEvaluationState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<EotEvaluationDto | null>(null)

  const evaluate = useCallback(
    async (payload?: EvaluateEotPayload) => {
      setState('evaluating')
      setError(null)
      try {
        const response = await evaluateEot(projectId, payload)
        setResult(response)
        setState('success')
        return response
      } catch (err) {
        setState('error')
        setError(err instanceof WeatherApiError ? err.message : 'ประเมิน EOT ไม่สำเร็จ')
        return null
      }
    },
    [projectId],
  )

  return { state, error, result, evaluate }
}
