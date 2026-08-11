import type { ChainStepTone } from '../payment/chainPermissions'
import type { UserRole } from '../../store/authStore'
import type { VariationOrderDto } from './types'

export type { ChainStepTone }

/**
 * "Show only the buttons the current user is actually entitled to" — the VO analogue of
 * `features/payment/chainPermissions.ts` (S9-FE-02 precedent, named explicitly as such by the
 * S10-FE-01 DoD). Deliberately a **separate module**, not a reuse of the payment one: the two
 * document types' status vocabularies differ (no `NotDue`; `Approved` is VO's terminal-success
 * state where Payment has `Certified`/`Paid`), and — per ADR-0016 — `Reject` is quorum-bound for the
 * VO from day one, which changes what "the final step's approver may reject" alone can promise the
 * UI (a reject may not actually terminate the document; see `useVariationOrderActions.ts`).
 *
 * **One real improvement over the Payment precedent.** `PaymentCertificateDto` exposes no
 * `AllowSelfApproval`, so `canAttemptApprove` there has to *guess* conservatively (assume `false`,
 * the tenant-policy default). `VariationOrderDto.allowSelfApproval` **is** on the wire (transcribed
 * from the real DTO — see `types.ts`), snapshotted at `Submit` time exactly like `approvalSteps`, so
 * this module reads it directly instead of guessing — a document whose pinned policy genuinely
 * opted into self-approval correctly shows the button to its own creator/submitter.
 *
 * **What this module still does NOT attempt**, for the same reason as the Payment precedent: role-
 * matching the current user against the resolved chain's per-step `requiredRole`. Unlike
 * `PaymentCertificateDto`, `VariationOrderDto.approvalSteps` *does* carry `requiredRole`/
 * `quorumCount` per step (transcribed from the real DTO) — but this module still cannot know
 * *whether the current user holds that role*, because no endpoint returns "what role does user X
 * have" for anyone other than the caller's own JWT claims, and even the caller's own current role
 * (`useAuthStore`) is a single value, not "every role this user has been granted" (`User.Role` is
 * single-valued server-side too, so this is not a frontend gap — see `chainPermissions.test.ts`).
 * Role-eligibility is left entirely to the server, which is the actual authorization boundary
 * regardless of what this UI shows or hides — an unauthorized click still surfaces the server's own
 * `not-current-step` 403 with a clear Thai message (`api.ts`).
 */

function isCreatorOrSubmitter(vo: VariationOrderDto, currentUserId: string | null): boolean {
  if (!currentUserId) return false
  return currentUserId === vo.createdByUserId || currentUserId === vo.submittedByUserId
}

/** `Draft` only — covers both a never-submitted VO and one just returned for revision
 * (`RevisionNo` distinguishes the two, editability does not — domain-rules.md §2.4). */
export function canAttemptSubmit(vo: VariationOrderDto): boolean {
  return vo.status === 'Draft'
}

/** `SetVariationContent`'s own guard (domain-rules.md §2.4): money/scope fields are editable only
 * while `Draft`. Used to show/hide the "แก้ไข" affordance before (re)submission. */
export function canAttemptEditContent(vo: VariationOrderDto): boolean {
  return vo.status === 'Draft'
}

export function canAttemptApprove(vo: VariationOrderDto, currentUserId: string | null): boolean {
  if (vo.status !== 'PendingApproval') return false
  if (vo.allowSelfApproval) return true
  return !isCreatorOrSubmitter(vo, currentUserId)
}

/** Shown whenever `PendingApproval`, regardless of step position — `ReturnForRevision` is available
 * to any holder of *any* pending step's role (domain-rules.md §2.3), deliberately not quorum-bound
 * (§8.4: it is the deadlock escape valve). */
export function canAttemptReturnForRevision(vo: VariationOrderDto): boolean {
  return vo.status === 'PendingApproval'
}

/** Structurally-final-step-only, matching `RejectVariationOrderCommandHandler`'s guard. Note this
 * says nothing about whether a click will actually *terminate* the document — ADR-0016 makes reject
 * quorum-bound, so a single reject on a `QuorumCount > 1` final step records a vote and leaves the
 * document `PendingApproval`; see `useVariationOrderActions.ts`'s reject-quorum handling. */
export function canAttemptReject(vo: VariationOrderDto): boolean {
  return vo.status === 'PendingApproval' && vo.totalSteps > 0 && vo.currentStepNo === vo.totalSteps
}

/** Mirrors `VariationOrder.Withdraw`'s aggregate-level guard (domain-rules.md §2.3): the submitter,
 * before any step has cleared. The aggregate's full rule additionally requires "zero votes cast this
 * revision" (matters when the first step's `QuorumCount > 1` — one vote can be recorded without
 * `currentStepNo` advancing at all), which is **not** derivable from this DTO alone (no vote-count
 * field — the same known gap `types.ts` documents for quorum progress generally). A withdraw blocked
 * by that half of the rule surfaces the server's own clear `VariationOrderWithdrawAfterVoteCast`
 * message (`api.ts`) rather than this function ever guessing. */
export function canAttemptWithdraw(vo: VariationOrderDto, currentUserId: string | null): boolean {
  if (vo.status !== 'PendingApproval') return false
  if (!currentUserId || currentUserId !== vo.submittedByUserId) return false
  return vo.currentStepNo <= 1
}

/** Mirrors `VariationOrder.Cancel`'s guard: `Draft` only, creator or a PM. */
export function canAttemptCancel(
  vo: VariationOrderDto,
  currentUserId: string | null,
  currentUserRole: UserRole | null,
): boolean {
  if (vo.status !== 'Draft') return false
  return currentUserId === vo.createdByUserId || currentUserRole === 'PM'
}

/** Visual tone for step `stepNo` (1-based) of the chain bar's step row. Pure function of the VO's
 * own `status`/`currentStepNo`/`totalSteps` — no history/role data needed. `Approved` is this
 * document type's terminal-success state (where Payment has `Certified`/`Paid`). */
export function resolveChainStepTone(vo: VariationOrderDto, stepNo: number): ChainStepTone {
  if (vo.status === 'Approved') return 'done'

  if (vo.status === 'Rejected') {
    // domain-rules.md §8: Reject only fires from the final step, so currentStepNo is that final
    // step at the moment of rejection — mark it (and only it) as the rejected rung.
    return stepNo === vo.currentStepNo ? 'rejected' : stepNo < vo.currentStepNo ? 'done' : 'pending'
  }

  if (stepNo < vo.currentStepNo) return 'done'
  if (stepNo === vo.currentStepNo) return 'current'
  return 'pending'
}
