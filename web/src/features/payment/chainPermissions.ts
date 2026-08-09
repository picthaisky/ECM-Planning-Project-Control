import type { PaymentCertificateDto } from './types'

/**
 * "Show only the buttons the current user is actually entitled to" (S9-FE-02 DoD) — computed from
 * exactly what `PaymentCertificateDto` exposes, never a guess dressed up as certainty.
 *
 * **What this module deliberately does NOT attempt:** role-matching against the resolved chain.
 * `PaymentCertificateDto` has no `ApprovalSteps` (no per-step `RequiredRole`/`QuorumCount` — see
 * `types.ts`'s own remarks), and the one endpoint that *could* approximate it —
 * `GET /api/v1/tenants/{tenantId}/approval-policies` — is `[Authorize(Roles = "Admin")]` at the
 * controller level (`TenantApprovalPoliciesController.cs:24`), so the QS/PM/ProjectDirector who
 * actually approve certificates cannot call it (403) even if this module tried. Reconstructing an
 * expected chain from data most approvers cannot read would be worse than not trying — a false
 * sense of precision. So role-eligibility is left entirely to the server, which is the actual
 * authorization boundary regardless of what this UI shows or hides (`PaymentCertificatesController`'s
 * own doc comment: "nothing on this controller ever grants authority by role name, only the resolved
 * chain does"); an unauthorized click still surfaces the server's own `not-current-step` 403 with a
 * clear Thai message (`api.ts`).
 *
 * **What this module DOES gate, because it is knowable for certain from the DTO alone:**
 * - Every action requires `status === 'PendingApproval'`.
 * - `Reject` additionally requires the certificate to be structurally at its final step
 *   (`currentStepNo === totalSteps`) — approval-workflow.md §4/§6.1: "only the final step's
 *   approver may reject; an intermediate approver may only ReturnForRevision."
 * - `Approve` additionally hides for the certificate's own creator/submitter — approval-workflow.md
 *   §6.1's separation-of-duties rule, applied with the *conservative* assumption that
 *   `AllowSelfApproval` is the tenant-policy default of `false` (approval-workflow.md §5.2), because
 *   the DTO does not expose the pinned policy's actual flag either. This can produce a false
 *   negative (hiding Approve for a tenant that genuinely opted into self-approval) but never a false
 *   positive that would surprise the user with a 403 — the safer direction to be wrong in. Flagged
 *   as a backend gap (surfacing `AllowSelfApproval` on the DTO would remove the need to guess) in
 *   this sprint's frontend report.
 * - `ReturnForRevision` is shown whenever the document is `PendingApproval` — the handler's own rule
 *   ("actor holds *any* pending step's role", approval-workflow.md §4) has no self-approval
 *   restriction and no final-step restriction, so no further narrowing is possible from the DTO.
 */

function isCreatorOrSubmitter(certificate: PaymentCertificateDto, currentUserId: string | null): boolean {
  if (!currentUserId) return false
  return currentUserId === certificate.createdByUserId || currentUserId === certificate.submittedByUserId
}

export function canAttemptApprove(certificate: PaymentCertificateDto, currentUserId: string | null): boolean {
  if (certificate.status !== 'PendingApproval') return false
  return !isCreatorOrSubmitter(certificate, currentUserId)
}

export function canAttemptReturnForRevision(certificate: PaymentCertificateDto): boolean {
  return certificate.status === 'PendingApproval'
}

export function canAttemptReject(certificate: PaymentCertificateDto): boolean {
  return (
    certificate.status === 'PendingApproval' &&
    certificate.totalSteps > 0 &&
    certificate.currentStepNo === certificate.totalSteps
  )
}

export type ChainStepTone = 'done' | 'current' | 'pending' | 'rejected'

/** Visual tone for step `stepNo` (1-based) of the chain bar's step row. Pure function of the
 * certificate's own `status`/`currentStepNo`/`totalSteps` — no history/role data needed. */
export function resolveChainStepTone(certificate: PaymentCertificateDto, stepNo: number): ChainStepTone {
  if (certificate.status === 'Certified' || certificate.status === 'Paid') return 'done'

  if (certificate.status === 'Rejected') {
    // approval-workflow.md §4/§6.1: Reject only fires from the final step, so currentStepNo is that
    // final step at the moment of rejection — mark it (and only it) as the rejected rung.
    return stepNo === certificate.currentStepNo ? 'rejected' : stepNo < certificate.currentStepNo ? 'done' : 'pending'
  }

  if (stepNo < certificate.currentStepNo) return 'done'
  if (stepNo === certificate.currentStepNo) return 'current'
  return 'pending'
}
