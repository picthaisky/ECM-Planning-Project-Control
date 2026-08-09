import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { EvmApiError, setEacVariantDefault } from './api'
import { VARIANT_SHORT_LABELS } from './evmSelectors'
import type { EacVariant, SetEacVariantDefaultResult } from './types'

export type SetEacDefaultState = 'idle' | 'saving' | 'error'

/**
 * Drives the S7-FE-03 "ตั้งเป็นค่าเริ่มต้นของโครงการ" action (US-7.2, ADR-0007(f)): calls the real
 * `PUT /api/v1/projects/{projectId}/eac-default` and tracks a saving/error status —
 * `SetEacDefaultButton` renders it. On success this also raises the same bottom-right toast pattern
 * `useRecalculateCpm.ts` established.
 */
export function useSetEacVariantDefault(projectId: string) {
  const [state, setState] = useState<SetEacDefaultState>('idle')
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(
    async (variant: EacVariant): Promise<SetEacVariantDefaultResult | null> => {
      setState('saving')
      setError(null)
      try {
        const result = await setEacVariantDefault(projectId, variant)
        setState('idle')
        pushToast({ message: `ตั้ง ${VARIANT_SHORT_LABELS[variant]} เป็นค่าเริ่มต้นของโครงการแล้ว` })
        return result
      } catch (err) {
        setState('error')
        setError(err instanceof EvmApiError ? err.message : 'ตั้งค่าเริ่มต้นของโครงการไม่สำเร็จ')
        return null
      }
    },
    [projectId],
  )

  return { state, error, save }
}
