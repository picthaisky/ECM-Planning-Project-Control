import { useCallback, useEffect, useMemo, useState } from 'react'
import { getVariationOrder, listVariationOrders, VoApiError } from './api'
import type { VariationOrderDto } from './types'

export type VariationOrdersLoadState = 'loading' | 'ready' | 'error'
export type VariationOrderReloadState = 'idle' | 'reloading' | 'error'

/**
 * Loads the S10-FE-01 VO register (`GET .../projects/{projectId}/variation-orders` — real, live,
 * unlike Payment Certificate's Sprint 9 equivalent) and owns "which VO is currently selected" for
 * the detail panel + `ApprovalChainBar` + `BacImpactPanel`, mirroring
 * `features/payment/usePaymentCertificates.ts`'s established shape.
 *
 * Deliberately the *single* place that mutates a `VariationOrderDto` in local state: every real
 * S10-BE-01/02/03 command already returns the fresh DTO as its response body, so
 * `applyUpdatedVo` patches both the selected VO and its row in `variationOrders` directly from that
 * response — never a re-fetch of the list for the interactive flow.
 */
export function useVariationOrders(projectId: string) {
  const [variationOrders, setVariationOrders] = useState<VariationOrderDto[]>([])
  const [loadState, setLoadState] = useState<VariationOrdersLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [reloadState, setReloadState] = useState<VariationOrderReloadState>('idle')
  const [reloadError, setReloadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await listVariationOrders(projectId)
      setVariationOrders(data)
      setLoadState('ready')
      setSelectedId((current) => current ?? data[0]?.id ?? null)
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof VoApiError ? error.message : 'โหลดรายการ Variation Order ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  const select = useCallback((id: string) => {
    setSelectedId(id)
    setReloadError(null)
  }, [])

  const clearSelection = useCallback(() => setSelectedId(null), [])

  /** Patches a VO returned by a real mutation (create/submit/approve/return/reject/withdraw/cancel/
   * update-content) into both `variationOrders` and the current selection — the DTO the server just
   * handed back is always more current than anything in local state, so it always wins. A brand-new
   * VO (create) is prepended; an existing one is replaced in place. */
  const applyUpdatedVo = useCallback((updated: VariationOrderDto) => {
    setVariationOrders((current) => {
      const exists = current.some((v) => v.id === updated.id)
      return exists ? current.map((v) => (v.id === updated.id ? updated : v)) : [updated, ...current]
    })
    setSelectedId(updated.id)
  }, [])

  const reloadSelected = useCallback(async () => {
    if (!selectedId) return null
    setReloadState('reloading')
    setReloadError(null)
    try {
      const updated = await getVariationOrder(selectedId)
      applyUpdatedVo(updated)
      setReloadState('idle')
      return updated
    } catch (error) {
      setReloadState('error')
      setReloadError(error instanceof VoApiError ? error.message : 'โหลด Variation Order นี้ใหม่ไม่สำเร็จ')
      return null
    }
  }, [selectedId, applyUpdatedVo])

  const selected = useMemo(
    () => variationOrders.find((v) => v.id === selectedId) ?? null,
    [variationOrders, selectedId],
  )

  return {
    variationOrders,
    loadState,
    loadError,
    reload: load,
    selected,
    selectedId,
    select,
    clearSelection,
    applyUpdatedVo,
    reloadSelected,
    reloadState,
    reloadError,
  }
}
