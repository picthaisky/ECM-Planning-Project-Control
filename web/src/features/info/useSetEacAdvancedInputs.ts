import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { ProjectApiError, setEacAdvancedInputs } from './api'
import type { ProjectEacConfig, SetEacAdvancedInputsPayload, SetEacAdvancedInputsResult } from './types'

export type SetEacAdvancedInputsState = 'idle' | 'saving' | 'error'

/**
 * Drives S14-FE-02's Project Info "ตั้งค่า EAC ขั้นสูง" save action (`PUT
 * .../eac-advanced-inputs`, S14-BE-03) — mirrors `features/evm/useSetEacVariantDefault.ts`'s shape
 * exactly (saving/error state, a success toast).
 *
 * `onSaved` is how the fresh result gets back into `useProjectMasterData`'s `ProjectDetail` without
 * this hook needing to own that state itself — `EacAdvancedInputsCard` passes
 * `updateEacConfig` from its own `useProjectMasterData(projectId)` instance.
 */
export function useSetEacAdvancedInputs(projectId: string) {
  const [state, setState] = useState<SetEacAdvancedInputsState>('idle')
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(
    async (
      payload: SetEacAdvancedInputsPayload,
      onSaved: (patch: Partial<ProjectEacConfig>) => void,
    ): Promise<SetEacAdvancedInputsResult | null> => {
      setState('saving')
      setError(null)
      try {
        const result = await setEacAdvancedInputs(projectId, payload)
        setState('idle')
        onSaved({
          eacManualEtc: result.eacManualEtc,
          eacCustomPerformanceFactor: result.eacCustomPerformanceFactor,
          eacManualEtcStaleSince: result.eacManualEtcStaleSince,
        })
        pushToast({ message: 'บันทึกค่า EAC ขั้นสูงแล้ว' })
        return result
      } catch (err) {
        setState('error')
        setError(err instanceof ProjectApiError ? err.message : 'บันทึกค่า EAC ขั้นสูงไม่สำเร็จ')
        return null
      }
    },
    [projectId],
  )

  const clearError = useCallback(() => setError(null), [])

  return { state, error, save, clearError }
}
