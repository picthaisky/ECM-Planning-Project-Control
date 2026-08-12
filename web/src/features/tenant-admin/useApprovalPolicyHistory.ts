import { useCallback, useEffect, useState } from 'react'
import { getApprovalPolicyHistory, TenantAdminApiError } from './api'
import type { ApprovalDocumentType, ApprovalPolicyVersionHistoryEntry } from './types'

export type ApprovalPolicyHistoryLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the full version timeline for one document type's tenant-wide policy (S15-BE-01,
 * `GET .../{documentType}/history`) — every version ever created, assembled server-side from
 * `ApprovalPolicy` + `AuditLog`, no new storage. Unlike `useApprovalPolicy.ts`'s `GET`, this
 * endpoint has no "not configured" 404 case to special-case — an empty array is itself a
 * legitimate, successful answer (`GetApprovalPolicyVersionHistoryQueryHandler`'s own remarks), so
 * this hook only distinguishes `ready` (possibly empty) from a genuine `error`.
 */
export function useApprovalPolicyHistory(tenantId: string, documentType: ApprovalDocumentType) {
  const [entries, setEntries] = useState<ApprovalPolicyVersionHistoryEntry[]>([])
  const [loadState, setLoadState] = useState<ApprovalPolicyHistoryLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getApprovalPolicyHistory(tenantId, documentType)
      setEntries(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof TenantAdminApiError ? error.message : 'โหลดประวัติเวอร์ชันนโยบายไม่สำเร็จ')
    }
  }, [tenantId, documentType])

  useEffect(() => {
    void load()
  }, [load])

  return { entries, loadState, loadError, reload: load }
}
