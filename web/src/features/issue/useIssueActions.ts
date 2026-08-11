import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { advanceIssueStatus, createIssue, IssueApiError } from './api'
import { ISSUE_STATUS_LABELS } from './issueLabels'
import type { CreateIssuePayload } from './types'

/**
 * Drives the two S11-BE-03 writes (`createIssue`/`advanceIssueStatus`) — hand-rolled busy/error
 * state, mirroring `features/vo/useVariationOrderActions.ts`'s established pattern.
 *
 * **Reloads the full list after every mutation, rather than patching the returned `IssueLogDto`
 * into local state.** Two independent reasons this matters here specifically:
 * 1. `statusCounts` (the tile numbers) is only ever returned by the list endpoint — a mutation
 *    response cannot refresh the tiles even if the caller wanted to patch in place.
 * 2. `AdvanceIssueStatusCommandHandler`/`CreateIssueCommandHandler` both construct their response
 *    with `sequenceNo: null` (`types.ts`'s remarks) — patching it into the table would blank out a
 *    real number the user was just looking at.
 * A full reload is the only way to keep the tiles and the table atomically consistent with the
 * server after a write, which is the actual DoD requirement.
 */
export function useIssueActions(projectId: string, onSaved: () => void) {
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  /** The single issue id currently mid-`advance-status`, if any — lets the table disable just that
   * row's button rather than the whole table while one request is in flight. */
  const [advancingId, setAdvancingId] = useState<string | null>(null)
  const [advanceError, setAdvanceError] = useState<string | null>(null)

  const clearCreateError = useCallback(() => setCreateError(null), [])
  const clearAdvanceError = useCallback(() => setAdvanceError(null), [])

  const create = useCallback(
    async (payload: CreateIssuePayload) => {
      setCreating(true)
      setCreateError(null)
      try {
        const saved = await createIssue(projectId, payload)
        onSaved()
        pushToast({ message: `แจ้งปัญหาใหม่เรียบร้อยแล้ว: ${saved.title}` })
        return saved
      } catch (error) {
        setCreateError(error instanceof IssueApiError ? error.message : 'แจ้งปัญหาใหม่ไม่สำเร็จ')
        return null
      } finally {
        setCreating(false)
      }
    },
    [projectId, onSaved],
  )

  const advance = useCallback(
    async (issueId: string) => {
      setAdvancingId(issueId)
      setAdvanceError(null)
      try {
        const saved = await advanceIssueStatus(projectId, issueId)
        onSaved()
        pushToast({ message: `เลื่อนสถานะ "${saved.title}" เป็น "${ISSUE_STATUS_LABELS[saved.status]}" แล้ว` })
        return saved
      } catch (error) {
        setAdvanceError(error instanceof IssueApiError ? error.message : 'เลื่อนสถานะไม่สำเร็จ')
        return null
      } finally {
        setAdvancingId(null)
      }
    },
    [projectId, onSaved],
  )

  return { creating, createError, clearCreateError, create, advancingId, advanceError, clearAdvanceError, advance }
}
