import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { createVariationOrder, updateVariationOrderContent, VoApiError } from './api'
import type { CreateVariationOrderPayload, UpdateVariationOrderContentPayload, VariationOrderDto } from './types'

/**
 * Drives the two real, live content-writing endpoints VO has that Payment Certificate does not
 * (S10-BE-01: `POST .../variation-orders` create, `PUT .../variation-orders/{id}/content`
 * re-price) — kept as its own small hook, separate from `useVariationOrderActions.ts`'s approval-
 * chain transitions, since creating/editing a VO is a project-scoped concern (no existing VO
 * required) rather than an action on one already-selected document.
 */
export function useVoContentSubmit(projectId: string, onSaved: (vo: VariationOrderDto) => void) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const create = useCallback(
    async (payload: CreateVariationOrderPayload) => {
      setBusy(true)
      setError(null)
      try {
        const created = await createVariationOrder(projectId, payload)
        onSaved(created)
        pushToast({ message: `สร้าง ${created.voNumber} เรียบร้อยแล้ว (ฉบับร่าง)` })
        return created
      } catch (err) {
        setError(err instanceof VoApiError ? err.message : 'สร้าง Variation Order ไม่สำเร็จ')
        return null
      } finally {
        setBusy(false)
      }
    },
    [projectId, onSaved],
  )

  const update = useCallback(
    async (id: string, payload: UpdateVariationOrderContentPayload) => {
      setBusy(true)
      setError(null)
      try {
        const updated = await updateVariationOrderContent(id, payload)
        onSaved(updated)
        pushToast({ message: `บันทึกการแก้ไข ${updated.voNumber} เรียบร้อยแล้ว` })
        return updated
      } catch (err) {
        setError(err instanceof VoApiError ? err.message : 'บันทึกการแก้ไขไม่สำเร็จ')
        return null
      } finally {
        setBusy(false)
      }
    },
    [onSaved],
  )

  return { busy, error, create, update, clearError: useCallback(() => setError(null), []) }
}
