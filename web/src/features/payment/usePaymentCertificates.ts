import { useCallback, useEffect, useMemo, useState } from 'react'
import { getPaymentCertificate, listPaymentCertificates, PaymentApiError } from './api'
import type { PaymentCertificateDto } from './types'

export type PaymentCertificatesLoadState = 'loading' | 'ready' | 'error'
export type PaymentCertificateReloadState = 'idle' | 'reloading' | 'error'

/**
 * Loads the S9-FE-01 milestone list (`GET .../projects/{projectId}/payment-certificates` — see
 * `api.ts#listPaymentCertificates`'s remarks: not implemented on the real backend yet) and owns
 * "which certificate is currently selected" for the certificate detail panel + `ApprovalChainBar`,
 * matching the prototype's "งวดงาน — คลิกเพื่อดู Certificate" click-to-select list
 * (`docs/ECM Planning Prototype.dc.html` line ~314).
 *
 * Deliberately the *single* place that mutates a `PaymentCertificateDto` in local state: every real
 * S9-BE-05 command (submit/approve/return-for-revision/reject/record-payment) already returns the
 * fresh DTO as its response body, so `applyUpdatedCertificate` patches both the selected certificate
 * and its row in `certificates` directly from that response — never a re-fetch of the (not yet real)
 * list/get endpoints for the interactive flow. `reloadSelected` is the one path that *does* call the
 * not-yet-real `getPaymentCertificate` (manual "โหลดใหม่", primarily useful after a `409
 * concurrent-transition`), and fails visibly rather than silently when that 404s today.
 */
export function usePaymentCertificates(projectId: string) {
  const [certificates, setCertificates] = useState<PaymentCertificateDto[]>([])
  const [loadState, setLoadState] = useState<PaymentCertificatesLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [reloadState, setReloadState] = useState<PaymentCertificateReloadState>('idle')
  const [reloadError, setReloadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await listPaymentCertificates(projectId)
      setCertificates(data)
      setLoadState('ready')
      setSelectedId((current) => current ?? data[0]?.id ?? null)
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof PaymentApiError ? error.message : 'โหลดรายการใบรับรองผลงานไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  const select = useCallback((id: string) => {
    setSelectedId(id)
    setReloadError(null)
  }, [])

  /** Patches a certificate returned by a real mutation into both `certificates` and the current
   * selection — the DTO the server just handed back is always more current than anything in local
   * state, so it always wins. */
  const applyUpdatedCertificate = useCallback((updated: PaymentCertificateDto) => {
    setCertificates((current) => {
      const exists = current.some((c) => c.id === updated.id)
      return exists ? current.map((c) => (c.id === updated.id ? updated : c)) : [updated, ...current]
    })
    setSelectedId(updated.id)
  }, [])

  const reloadSelected = useCallback(async () => {
    if (!selectedId) return null
    setReloadState('reloading')
    setReloadError(null)
    try {
      const updated = await getPaymentCertificate(selectedId)
      applyUpdatedCertificate(updated)
      setReloadState('idle')
      return updated
    } catch (error) {
      setReloadState('error')
      setReloadError(error instanceof PaymentApiError ? error.message : 'โหลดใบรับรองผลงานนี้ใหม่ไม่สำเร็จ')
      return null
    }
  }, [selectedId, applyUpdatedCertificate])

  const selected = useMemo(
    () => certificates.find((c) => c.id === selectedId) ?? null,
    [certificates, selectedId],
  )

  return {
    certificates,
    loadState,
    loadError,
    reload: load,
    selected,
    selectedId,
    select,
    applyUpdatedCertificate,
    reloadSelected,
    reloadState,
    reloadError,
  }
}
