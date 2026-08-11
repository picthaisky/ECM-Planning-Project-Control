/**
 * Wire shapes for the S10-BE-01/02/03 Variation Order feature, transcribed from the real source,
 * not assumed: `backend/src/CMPlus.Application/Features/VariationOrder/VariationOrderDto.cs`,
 * `.../VariationOrderErrorCodes.cs`, `backend/src/CMPlus.Domain/Enums/VariationOrderStatus.cs`,
 * `VariationOrderType.cs`, `backend/src/CMPlus.Domain/Entities/VariationOrderScopeItem.cs`
 * (`VariationOrderScopeItemInput`), `backend/src/CMPlus.WebApi/Controllers/VariationOrder/
 * VariationOrderRequests.cs`.
 *
 * Every backend `decimal`/`decimal?` (money and percent alike) is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare number, so a
 * client can never lose precision through a JS float. Every field below that mirrors a backend
 * `decimal`/`decimal?` is typed `string`/`string | null` for that reason; parse with `Number(...)`
 * only at the point of doing arithmetic/formatting (`utils/format.ts`, `bacImpact.ts`), never
 * compare/store as a JS number across a render (mirrors `features/payment/types.ts`'s identical
 * remark).
 *
 * `VariationOrderStatus`/`VariationOrderType`/`UserRole` arrive as their enum *names* (project-wide
 * `JsonStringEnumConverter`), never numbers. `ApprovalActionType`/`ApprovalActionDto` are reused
 * verbatim from `../payment/types` — the backend's `ApprovalActionDto` (`Features/Approval/Queries/
 * GetApprovalActionHistory/ApprovalActionDto.cs`) is explicitly "document-type-agnostic... Sprint
 * 10's VariationOrder history reuses this DTO... unchanged", so there is nothing VO-specific to
 * redeclare (conventions.md "one fact lives in one place").
 */

import type { UserRole } from '../../store/authStore'

export type { UserRole }
export type { ApprovalActionDto, ApprovalActionType, ApprovalCommentPayload } from '../payment/types'

/** `CMPlus.Domain.Enums.VariationOrderStatus` — the five-state machine (domain-rules.md §2).
 * `Approved`/`Rejected`/`Cancelled` are terminal — no transition ever leaves them. There is no
 * `NotDue` (unlike `PaymentCertificateStatus`): a VO exists from the moment it is drafted. */
export type VariationOrderStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled'

/** `CMPlus.Domain.Enums.VariationOrderType` — derived from `sign(Amount)` server-side, never
 * independently settable (domain-rules.md §1). Zero is grouped with `Add`. */
export type VariationOrderType = 'Add' | 'Deduct'

/** One rung of `VariationOrderDto.approvalSteps` — the snapshotted chain exactly as resolved at
 * `Submit` time (H-01 fix pattern), never re-derived from the live policy. */
export interface VariationOrderApprovalStepDto {
  stepNo: number
  requiredRole: UserRole
  quorumCount: number
}

/** One line of `VariationOrderDto.scopeItems` — the budget-delta payload whose net must equal
 * `amount` exactly (domain-rules.md §5.2's $\Delta B_{scope} = A$ invariant). */
export interface VariationOrderScopeItemDto {
  activityId: string
  budgetCostDelta: string
  note: string | null
}

/**
 * `VariationOrderDto` — the shared response shape for every S10-BE-01/02/03 command (create/submit/
 * approve/return-for-revision/reject/withdraw/cancel/update-content) and the real, live
 * `GET /api/v1/variation-orders/{id}` / `GET /api/v1/projects/{projectId}/variation-orders` reads.
 *
 * `bacBefore`/`bacAfter`/`contractValueBefore`/`contractValueAfter`/`cumulativeVoPctAtApproval`/
 * `escalationBasisContractValue` are **null until `Approved`, written once, then immutable**
 * (domain-rules.md §2.4 freeze matrix) — never available for a preview before that transition, which
 * is exactly why `components/BacImpactPanel.tsx` computes its own client-side projection from the
 * *current* project figures rather than reading these fields for a `Draft`/`PendingApproval` VO.
 *
 * Deliberately does **not** include a `quorumSplit` flag or per-step vote-progress counts —
 * domain-rules.md §8.4 names both as what should close the "a first approver cannot tell their vote
 * from a no-op" gap, but `backend-developer` deferred them this sprint (see this feature's frontend
 * report). `useVariationOrderActions.ts`'s before/after status diff (mirroring
 * `usePaymentCertificateActions.ts`'s S9 precedent) is what stands in for it here.
 */
export interface VariationOrderDto {
  id: string
  projectId: string
  voNumber: string
  description: string | null
  justification: string | null
  /** $A$ — signed. `Add` ⟹ positive, `Deduct` ⟹ negative, zero permitted (a time-only variation). */
  amount: string
  type: VariationOrderType
  timeImpactDays: number
  status: VariationOrderStatus
  revisionNo: number
  currentStepNo: number
  totalSteps: number
  approvalPolicyId: string | null
  approvalPolicyVersion: number | null
  allowSelfApproval: boolean
  approvalSteps: VariationOrderApprovalStepDto[]
  scopeItems: VariationOrderScopeItemDto[]
  createdByUserId: string
  submittedByUserId: string | null
  submittedAt: string | null
  approvedAt: string | null
  bacBefore: string | null
  bacAfter: string | null
  contractValueBefore: string | null
  contractValueAfter: string | null
  cumulativeVoPctAtApproval: string | null
  escalationBasisContractValue: string | null
}

/** `VariationOrderScopeItemInput` (`CMPlus.Domain.Entities.VariationOrderScopeItem`) — one line of
 * a create/update-content request body. */
export interface VariationOrderScopeItemInput {
  activityId: string
  budgetCostDelta: string
  note: string | null
}

/** `CreateVariationOrderRequest` — `POST /api/v1/projects/{projectId}/variation-orders`. */
export interface CreateVariationOrderPayload {
  voNumber: string
  description: string | null
  justification: string | null
  amount: string
  timeImpactDays: number
  scopeItems: VariationOrderScopeItemInput[]
}

/** `UpdateVariationOrderContentRequest` — `PUT /api/v1/variation-orders/{id}/content` (re-pricing a
 * returned-for-revision document, or editing a never-submitted Draft, before (re)submission). */
export interface UpdateVariationOrderContentPayload {
  description: string | null
  justification: string | null
  amount: string
  timeImpactDays: number
  scopeItems: VariationOrderScopeItemInput[]
}
