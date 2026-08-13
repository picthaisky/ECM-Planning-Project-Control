/**
 * Wire shapes for the S9-BE-01/02/04/05 Payment Certificate approval-chain feature, transcribed
 * from the real source, not assumed:
 * `backend/src/CMPlus.Application/Features/Payment/PaymentCertificateDto.cs`,
 * `.../PaymentApprovalErrorCodes.cs`, `backend/src/CMPlus.Domain/Enums/PaymentCertificateStatus.cs`,
 * `backend/src/CMPlus.Domain/Enums/ApprovalActionType.cs`,
 * `backend/src/CMPlus.Domain/Entities/ApprovalAction.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Payment/{ApprovalCommentRequest,RecordPaymentRequest}.cs`.
 *
 * Every backend `decimal`/`decimal?` (money and percent alike) is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare number, so a
 * client can never lose precision through a JS float. Every field below that mirrors a backend
 * `decimal`/`decimal?` is typed `string`/`string | null` for exactly that reason; parse with
 * `Number(...)` only at the point of doing arithmetic/formatting (`utils/format.ts`), never
 * compare/store as a JS number across a render (mirrors `features/evm/types.ts`'s identical remark).
 *
 * `PaymentCertificateStatus`/`ApprovalActionType` arrive as their enum *names* (project-wide
 * `JsonStringEnumConverter`), never numbers.
 */

import type { UserRole } from '../../store/authStore'

export type { UserRole }

/** `CMPlus.Domain.Enums.PaymentCertificateStatus` — the 7-state machine
 * (approval-workflow.md §3). `Certified` is not terminal: `Paid` records the actual disbursement. */
export type PaymentCertificateStatus =
  | 'NotDue'
  | 'Draft'
  | 'PendingApproval'
  | 'Certified'
  | 'Paid'
  | 'Rejected'
  | 'Cancelled'

/**
 * `PaymentCertificateDto` — the shared response shape for every S9-BE-05 workflow command
 * (submit/approve/return-for-revision/reject/record-payment) *and* the live
 * `GET /api/v1/payment-certificates/{id}` read (`api.ts#getPaymentCertificate`).
 *
 * Deliberately does **not** include `AllowSelfApproval` or `ApprovalSteps` (role/quorum per step) —
 * the real `PaymentCertificate` aggregate has both (`backend/src/CMPlus.Domain/Entities/
 * PaymentCertificate.cs:119,125`), but `PaymentCertificateDto.cs` does not project them onto the
 * wire. `components/ApprovalChainBar.tsx` and its button-visibility logic are built around this
 * exact gap — see that file's own remarks — rather than guessing at a shape that does not exist.
 */
export interface PaymentCertificateDto {
  id: string
  projectId: string
  milestoneNo: number
  description: string | null
  milestoneValue: string
  previousCumulativeApprovePct: string
  approvePct: string
  claimPct: string | null
  actualProgressPct: string | null
  grossCertifiedAmount: string
  retentionAmount: string
  advanceRecoveryAmount: string
  netPayment: string
  status: PaymentCertificateStatus
  revisionNo: number
  currentStepNo: number
  totalSteps: number
  approvalPolicyId: string | null
  approvalPolicyVersion: number | null
  createdByUserId: string
  submittedByUserId: string | null
  submittedAt: string | null
  certifiedAt: string | null
  paidAt: string | null
  paymentReference: string | null
}

/** `CMPlus.Domain.Enums.ApprovalActionType` — every human act the append-only `ApprovalAction`
 * ledger records. `Submit`/`Withdraw`/`Cancel` are not step-specific (`stepNo: 0` on the wire). */
export type ApprovalActionType =
  | 'Submit'
  | 'Approve'
  | 'ReturnForRevision'
  | 'Reject'
  | 'Withdraw'
  | 'Cancel'
  | 'RecordPayment'

/**
 * `ApprovalAction` — one row of the append-only approval-history ledger
 * (`backend/src/CMPlus.Domain/Entities/ApprovalAction.cs`). There is **no** `ApprovalActionDto` in
 * the real backend and **no** `GET /api/v1/payment-certificates/{id}/approval-actions` controller
 * action yet, even though it is documented as accepted contract
 * (`docs/specs/master-plan/design.md` §2.3: "GET /api/v1/{…}/{id}/approval-actions → append-only
 * history") and flagged as a tracked gap by the Sprint 9 security review (L-04: "S9-FE-02's chain
 * bar will need this before the quorum feature is usable"). This type is transcribed field-for-field
 * from the real `ApprovalAction` entity (never guessed) so `components/ApprovalChainBar.tsx` and
 * `useApprovalActions.ts` are ready to render real data the moment that endpoint ships — see
 * `api.ts#getApprovalActions`'s remarks.
 */
export interface ApprovalActionDto {
  id: string
  documentType: 'PaymentCertificate' | 'VariationOrder'
  documentId: string
  revisionNo: number
  stepNo: number
  actorUserId: string
  actorRoleAtTime: UserRole
  action: ApprovalActionType
  comment: string | null
  actedAt: string
  approvalPolicyId: string
  approvalPolicyVersion: number
}

/** `POST /api/v1/payment-certificates/{id}/{approve|return-for-revision|reject}` request body
 * (`ApprovalCommentRequest`) — identical shape for all three; whether `comment` is actually
 * mandatory is a per-action rule enforced server-side by each command's own validator (mandatory
 * for reject/return-for-revision, optional for approve — mirrored client-side in
 * `components/ApprovalActionModal.tsx` so the user never round-trips to discover it). */
export interface ApprovalCommentPayload {
  comment: string | null
}

/** `POST /api/v1/payment-certificates/{id}/record-payment` request body (`RecordPaymentRequest`). */
export interface RecordPaymentPayload {
  reference: string
  /** ISO 8601 instant (`DateTimeOffset`). */
  paidAt: string
}

/**
 * `POST /api/v1/projects/{projectId}/payment-certificates` request body (S9-BE-05,
 * `CreatePaymentCertificateRequest`). Decimals are sent as JSON numbers (the server binds them to
 * `decimal`). `previousCumulativeApprovePct` is deliberately absent - the server auto-derives it from
 * the project's prior `Certified`/`Paid` certificates for this same milestone, never a client input.
 */
export interface CreatePaymentCertificatePayload {
  milestoneNo: number
  description?: string | null
  milestoneValue: number
  /** Cumulative certified progress % this period (0-100); must be >= the server-derived previous. */
  thisCumulativeApprovePct: number
  claimPct?: number | null
  actualProgressPct?: number | null
  /** Only used when the project's advance-recovery method is Manual. */
  manualAdvanceRecoveryAmount?: number | null
}
