import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { ApprovalDocumentType, ApprovalPolicy, UpdateApprovalPolicyPayload } from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/payment/api.ts`'s `PaymentApiError`/`features/evm/api.ts`'s `EvmApiError`. */
export class TenantAdminApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'TenantAdminApiError'
    this.status = status
  }
}

/** `design.md` §2.2: `PUT .../approval-policies/{documentType}` failing band validation returns
 * `400` with `{ invalidStepNo, problem: "BandOverlap" | "BandGap" }` as `ProblemDetails.Extensions`
 * members (`ResultProblemMapper.cs`'s `bandProblem.Extensions[...]`, which ASP.NET Core's
 * `[JsonExtensionData]` on `ProblemDetails.Extensions` serializes as flat sibling JSON properties,
 * not nested). Thrown instead of the generic `TenantAdminApiError` so the editor can point at the
 * exact offending `StepNo` even when the *server's* rejection differs from this form's own inline
 * `bandValidation.ts` pre-check (e.g. a stale client cache, or a genuine algorithm disagreement).
 */
export class ApprovalPolicyBandError extends TenantAdminApiError {
  readonly problem: 'BandOverlap' | 'BandGap'
  readonly invalidStepNo: number | null

  constructor(message: string, problem: 'BandOverlap' | 'BandGap', invalidStepNo: number | null, status?: number) {
    super(message, status)
    this.name = 'ApprovalPolicyBandError'
    this.problem = problem
    this.invalidStepNo = invalidStepNo
  }
}

const TENANT_ADMIN_ERROR_TITLES: Record<string, string> = {
  ApprovalPolicyNotFound: 'ยังไม่มีการตั้งค่านโยบายอนุมัติสำหรับประเภทเอกสารนี้',
  'not-found': 'ยังไม่มีการตั้งค่านโยบายอนุมัติสำหรับประเภทเอกสารนี้',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const TENANT_ADMIN_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

interface BandProblemBody extends ProblemDetails {
  problem?: 'BandOverlap' | 'BandGap'
  invalidStepNo?: number
}

function toTenantAdminApiError(error: unknown): TenantAdminApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && TENANT_ADMIN_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    return new TenantAdminApiError(TENANT_ADMIN_ERROR_TITLES[code] ?? TENANT_ADMIN_GENERIC_ERROR_MESSAGE, status)
  }
  return new TenantAdminApiError(TENANT_ADMIN_GENERIC_ERROR_MESSAGE)
}

/**
 * `GET /api/v1/tenants/{tenantId}/approval-policies?documentType=` (S2-BE-07) — real, live endpoint.
 * `Admin`-only server-side (`TenantApprovalPoliciesController`'s class-level
 * `[Authorize(Roles = "Admin")]`) — a non-Admin gets a bodyless 403 from the framework's own role
 * challenge; a wrong-tenant `tenantId` route value gets a bare 404 (design.md §2.2: "never confirm
 * another tenant exists"), which surfaces here as the generic "not configured yet" message, same as
 * a genuinely unconfigured document type — `useApprovalPolicy.ts` treats any 404 as "no policy yet,
 * start from a blank draft" rather than a hard error, since both cases are equally actionable for an
 * Admin (create the first version).
 */
export async function getApprovalPolicy(tenantId: string, documentType: ApprovalDocumentType): Promise<ApprovalPolicy> {
  try {
    const response = await apiClient.get<ApprovalPolicy>(`/tenants/${tenantId}/approval-policies`, {
      params: { documentType },
    })
    return response.data
  } catch (error) {
    throw toTenantAdminApiError(error)
  }
}

/**
 * `PUT /api/v1/tenants/{tenantId}/approval-policies/{documentType}` (S9-BE-06) — real, live
 * endpoint. Always creates `Version + 1` and deactivates the previous version server-side
 * (`UpdateApprovalPolicyCommandHandler`) — the response's `version` field is the new one, which
 * `useUpdateApprovalPolicy.ts` surfaces directly (S9-FE-03 DoD: "บันทึกแล้วเห็น version ใหม่").
 */
export async function updateApprovalPolicy(
  tenantId: string,
  documentType: ApprovalDocumentType,
  payload: UpdateApprovalPolicyPayload,
): Promise<ApprovalPolicy> {
  try {
    const response = await apiClient.put<ApprovalPolicy>(
      `/tenants/${tenantId}/approval-policies/${documentType}`,
      payload,
    )
    return response.data
  } catch (error) {
    if (error instanceof AxiosError && error.response?.status === 400) {
      const body = error.response.data as BandProblemBody | undefined
      if (body?.problem === 'BandOverlap' || body?.problem === 'BandGap') {
        const stepNo = typeof body.invalidStepNo === 'number' ? body.invalidStepNo : null
        const message =
          body.problem === 'BandOverlap'
            ? `พบช่วงจำนวนเงินที่ทับซ้อนกันในขั้นตอนที่ ${stepNo ?? '-'}  กรุณาแก้ไขให้แต่ละขั้นตอนครอบคลุมช่วงจำนวนเงินไม่ซ้อนกัน`
            : `พบช่วงจำนวนเงินที่ขาดหายไปสำหรับขั้นตอนที่ ${stepNo ?? '-'} กรุณาตรวจสอบให้ขั้นตอนต่อเนื่องกันโดยไม่มีช่องว่าง`
        throw new ApprovalPolicyBandError(message, body.problem, stepNo, 400)
      }
    }
    throw toTenantAdminApiError(error)
  }
}
