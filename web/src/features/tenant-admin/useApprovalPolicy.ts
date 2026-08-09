import { useCallback, useEffect, useState } from 'react'
import { getApprovalPolicy, TenantAdminApiError } from './api'
import type { ApprovalDocumentType, ApprovalPolicy } from './types'

export type ApprovalPolicyLoadState = 'loading' | 'ready' | 'not-configured' | 'error'

/**
 * Loads the currently active policy for one document type (S9-FE-03). A `404` is split into two
 * distinct outcomes rather than one generic error state: `ApprovalPolicyNotFound` (this tenant
 * genuinely has no policy of this type yet — `PUT` will create `Version 1`, an entirely normal,
 * actionable state for an Admin, not a failure) versus every other failure (`'error'`, including a
 * cross-tenant 404, which is indistinguishable from "not configured" by design — design.md §2.2 —
 * and is therefore treated the same way here, which is also the safe direction: worst case, an
 * Admin who mistyped a tenant scope sees a blank draft rather than a leaked cross-tenant error).
 */
export function useApprovalPolicy(tenantId: string, documentType: ApprovalDocumentType) {
  const [policy, setPolicy] = useState<ApprovalPolicy | null>(null)
  const [loadState, setLoadState] = useState<ApprovalPolicyLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getApprovalPolicy(tenantId, documentType)
      setPolicy(data)
      setLoadState('ready')
    } catch (error) {
      if (error instanceof TenantAdminApiError && error.status === 404) {
        setPolicy(null)
        setLoadState('not-configured')
        return
      }
      setLoadState('error')
      setLoadError(error instanceof TenantAdminApiError ? error.message : 'โหลดนโยบายอนุมัติไม่สำเร็จ')
    }
  }, [tenantId, documentType])

  useEffect(() => {
    void load()
  }, [load])

  return { policy, loadState, loadError, reload: load }
}
