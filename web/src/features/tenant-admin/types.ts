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

/**
 * S15-BE-01 wire shapes, transcribed from the real source: `backend/src/CMPlus.Application/
 * Features/Approval/Queries/{GetApprovalPolicyVersionHistory/ApprovalPolicyVersionHistoryEntryDto,
 * SimulateRouting/ApprovalRoutingSimulationDto}.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Approval/{TenantApprovalPoliciesController,
 * SimulateApprovalRoutingRequest}.cs`.
 */

/** One row of the tenant-wide policy's version timeline (`GET .../{documentType}/history`) —
 * assembled server-side from `ApprovalPolicy` (every version, never deleted) + `AuditLog`
 * (who/when) — no new storage. `createdByUserId`/`createdAt`/`lastModifiedByUserId`/
 * `lastModifiedAt` are `null` when the version was created by a path that bypasses
 * `AuditSaveChangesInterceptor` (e.g. the tenant-provisioning seeder) — read as "unknown", never
 * fabricated. */
export interface ApprovalPolicyVersionHistoryEntry {
  approvalPolicyId: string
  version: number
  isActive: boolean
  effectiveFrom: string
  effectiveTo: string | null
  allowSelfApproval: boolean
  cumulativeVoEscalationPct: string | null
  cumulativeVoEscalationRole: UserRole | null
  ruleCount: number
  createdByUserId: string | null
  createdAt: string | null
  lastModifiedByUserId: string | null
  lastModifiedAt: string | null
}

/** One rung of a simulated chain (`SimulatedApprovalChainStepDto`). */
export interface SimulatedApprovalChainStep {
  stepNo: number
  requiredRole: UserRole
  quorumCount: number
}

/** One of the ADR-0021 simultaneously-active policy rows detected in the simulated scope
 * (`AmbiguousActivePolicyDto`) — present only when `multipleActivePoliciesDetected` is `true`. */
export interface AmbiguousActivePolicy {
  approvalPolicyId: string
  version: number
}

/** `POST /api/v1/tenants/{tenantId}/approval-policies/{documentType}/simulate` request body
 * (`SimulateApprovalRoutingRequest`). `projectId` is mandatory — escalation (VariationOrder only)
 * needs a real project's baseline contract value, so "no project" is never a valid simulation. */
export interface SimulateApprovalRoutingPayload {
  projectId: string
  amount: string
}

/** `POST .../{documentType}/simulate` 200 response (`ApprovalRoutingSimulationDto`) — the exact
 * chain a real Submit would resolve right now against `approvalPolicyVersion`, without creating any
 * document. `multipleActivePoliciesDetected`/`ambiguousActivePolicies` surface ADR-0021's known
 * corruption (two simultaneously-active tenant-wide policies) rather than hiding it. */
export interface ApprovalRoutingSimulation {
  documentType: ApprovalDocumentType
  projectId: string
  inputAmount: string
  routingAmount: string
  approvalPolicyId: string
  approvalPolicyVersion: number
  usedFallbackChain: boolean
  steps: SimulatedApprovalChainStep[]
  escalationApplied: boolean
  allowSelfApproval: boolean
  multipleActivePoliciesDetected: boolean
  ambiguousActivePolicies: AmbiguousActivePolicy[]
}
