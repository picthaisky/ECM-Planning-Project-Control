import { useCallback, useState } from 'react'
import { ApprovalPolicyBandError, TenantAdminApiError, updateApprovalPolicy } from './api'
import type { ApprovalDocumentType, ApprovalPolicy, UpdateApprovalPolicyPayload } from './types'

export type UpdateApprovalPolicySaveState = 'idle' | 'saving' | 'error'

/**
 * Drives the S9-FE-03 save action. `savedVersion` is the DoD's "บันทึกแล้วเห็น version ใหม่" —
 * lifted straight from the real `PUT` response (`ApprovalPolicyDto.version`, always `previous + 1`
 * server-side, `UpdateApprovalPolicyCommandHandler`), never computed client-side. `bandError` is
 * kept distinct from `saveError` (a plain string) so the editor can also highlight the exact
 * offending `StepNo` the *server* rejected — normally redundant with this form's own inline
 * `bandValidation.ts` pre-check, but the server remains the authority if the two ever disagree.
 */
export function useUpdateApprovalPolicy(tenantId: string, documentType: ApprovalDocumentType) {
  const [saveState, setSaveState] = useState<UpdateApprovalPolicySaveState>('idle')
  const [saveError, setSaveError] = useState<string | null>(null)
  const [bandError, setBandError] = useState<ApprovalPolicyBandError | null>(null)
  const [savedVersion, setSavedVersion] = useState<number | null>(null)

  const save = useCallback(
    async (payload: UpdateApprovalPolicyPayload): Promise<ApprovalPolicy | null> => {
      setSaveState('saving')
      setSaveError(null)
      setBandError(null)
      try {
        const updated = await updateApprovalPolicy(tenantId, documentType, payload)
        setSaveState('idle')
        setSavedVersion(updated.version)
        return updated
      } catch (error) {
        setSaveState('error')
        if (error instanceof ApprovalPolicyBandError) {
          setBandError(error)
          setSaveError(error.message)
        } else {
          setSaveError(error instanceof TenantAdminApiError ? error.message : 'บันทึกนโยบายอนุมัติไม่สำเร็จ')
        }
        return null
      }
    },
    [tenantId, documentType],
  )

  const clearSavedVersion = useCallback(() => setSavedVersion(null), [])

  return { save, saveState, saveError, bandError, savedVersion, clearSavedVersion }
}
