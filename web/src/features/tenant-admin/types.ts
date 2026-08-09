/**
 * Wire shapes for the S2-BE-07/S9-BE-06 Tenant Admin approval-policy read/write API, transcribed
 * from the real source, not assumed:
 * `backend/src/CMPlus.Application/Features/Approval/Queries/GetApprovalPolicy/ApprovalPolicyDto.cs`,
 * `.../Commands/UpdateApprovalPolicy/UpdateApprovalPolicyCommand.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Approval/{TenantApprovalPoliciesController,
 * UpdateApprovalPolicyRequest}.cs`, `backend/src/CMPlus.Domain/Enums/ApprovalDocumentType.cs`.
 *
 * `StepNo`/`QuorumCount` are `int` on the backend — plain numbers on the wire, unlike every money/
 * percent `decimal` field (`MinAmount`/`MaxAmount`/`CumulativeVoEscalationPct`), which are JSON
 * *strings* (`DecimalAsStringJsonConverter`, project-wide) for the same lossless-precision reason
 * `features/evm/types.ts`/`features/info/types.ts` document. `ApprovalDocumentType`/`UserRole`
 * arrive as their enum *names* (project-wide `JsonStringEnumConverter`), never numbers.
 */

import type { UserRole } from '../../store/authStore'

export type { UserRole }

/** `CMPlus.Domain.Enums.ApprovalDocumentType`. */
export type ApprovalDocumentType = 'VariationOrder' | 'PaymentCertificate'

/** One row of the amount-tiered matrix (`ApprovalPolicyRuleDto` / `UpdateApprovalPolicyRuleRequest`). */
export interface ApprovalPolicyRule {
  stepNo: number
  /** Inclusive lower bound. */
  minAmount: string
  /** Exclusive upper bound; `null` = unbounded. */
  maxAmount: string | null
  requiredRole: UserRole
  /** Default `1` server-side. Schema-present but **not server-bounded** (security review
   * sprint-09.md N-02) — see `bandValidation.ts#MAX_CLIENT_QUORUM_COUNT`'s remarks. */
  quorumCount: number
}

/** `GET /api/v1/tenants/{tenantId}/approval-policies?documentType=` response body
 * (design.md §2.2). */
export interface ApprovalPolicy {
  documentType: ApprovalDocumentType
  version: number
  isActive: boolean
  allowSelfApproval: boolean
  /** `VariationOrder` policies only (approval-workflow.md §5.2) — `null` disables escalation. */
  cumulativeVoEscalationPct: string | null
  cumulativeVoEscalationRole: UserRole | null
  rules: ApprovalPolicyRule[]
}

/** `PUT /api/v1/tenants/{tenantId}/approval-policies/{documentType}` request body
 * (`UpdateApprovalPolicyRequest`) — same field set as `ApprovalPolicy` minus the server-assigned
 * `documentType`/`version`/`isActive` (the route segment carries `documentType`; `version` is
 * always `current + 1`, decided server-side, never supplied by the client). */
export interface UpdateApprovalPolicyPayload {
  allowSelfApproval: boolean
  cumulativeVoEscalationPct: string | null
  cumulativeVoEscalationRole: UserRole | null
  rules: ApprovalPolicyRule[]
}
