import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  ApprovalActionDto,
  ApprovalCommentPayload,
  PaymentCertificateDto,
  RecordPaymentPayload,
} from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/evm/api.ts`'s `EvmApiError`/`features/wbs/api.ts`'s `WbsApiError`. */
export class PaymentApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'PaymentApiError'
    this.status = status
  }
}

/** `PaymentApprovalErrorCodes` (backend) + the reused Sprint 2 `ApprovalErrorCodes.PolicyGap` —
 * transcribed from `ResultProblemMapper.cs`'s `KnownErrors` table, not guessed. Keyed by both the
 * stable `detail` code and the `type` URL's trailing slug, same double-keying discipline as
 * `features/evm/api.ts#EVM_ERROR_TITLES`, since `toPaymentApiError` below tries `detail` first and
 * falls back to the `type` slug. */
const PAYMENT_ERROR_TITLES: Record<string, string> = {
  PaymentCertificateNotFound: 'ไม่พบใบรับรองผลงานที่ระบุ',
  'not-found': 'ไม่พบใบรับรองผลงานที่ระบุ',

  PaymentCertificateInvalidStatusForTransition:
    'เอกสารไม่อยู่ในสถานะที่ทำรายการนี้ได้ กรุณาโหลดข้อมูลล่าสุดแล้วลองใหม่',
  'document-immutable': 'เอกสารไม่อยู่ในสถานะที่ทำรายการนี้ได้ กรุณาโหลดข้อมูลล่าสุดแล้วลองใหม่',

  PaymentCertificateNotAuthorizedForApprovalStep: 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',
  'not-current-step': 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',

  PaymentCertificateSelfApprovalNotPermitted: 'ผู้สร้างหรือผู้ยื่นเอกสารไม่สามารถอนุมัติเอกสารฉบับของตนเองได้',
  'self-approval-not-permitted': 'ผู้สร้างหรือผู้ยื่นเอกสารไม่สามารถอนุมัติเอกสารฉบับของตนเองได้',

  // ADR-0016 (2026-08-10) / backend PaymentApprovalErrorCodes.cs: renamed from
  // PaymentCertificateDuplicateChainApprover / 'duplicate-chain-approver' - widened from
  // Approve-only to Approve-or-Reject ("no actor may both approve and reject the same revision"),
  // so the code and the Thai copy below can no longer say only "อนุมัติ" (approve).
  PaymentCertificateDuplicateChainVoter: 'คุณได้อนุมัติหรือปฏิเสธขั้นตอนอื่นของเอกสารฉบับนี้ไปแล้ว ไม่สามารถลงมติซ้ำได้',
  'duplicate-chain-voter': 'คุณได้อนุมัติหรือปฏิเสธขั้นตอนอื่นของเอกสารฉบับนี้ไปแล้ว ไม่สามารถลงมติซ้ำได้',

  PaymentCertificateConcurrencyConflict: 'มีการเปลี่ยนแปลงเอกสารนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'concurrent-transition': 'มีการเปลี่ยนแปลงเอกสารนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',

  PaymentCertificateCorruptApprovalChain: 'ไม่สามารถประมวลผลลำดับการอนุมัติของเอกสารนี้ได้ กรุณาติดต่อผู้ดูแลระบบ',
  'corrupt-approval-chain': 'ไม่สามารถประมวลผลลำดับการอนุมัติของเอกสารนี้ได้ กรุณาติดต่อผู้ดูแลระบบ',

  // Sprint 2 ApprovalErrorCodes.PolicyGap, reused as-is by Submit (design.md §1.2 "fail-closed").
  ApprovalPolicyGap: 'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',
  'approval-policy-gap':
    'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const PAYMENT_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toPaymentApiError(error: unknown): PaymentApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && PAYMENT_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/evm/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new PaymentApiError(PAYMENT_ERROR_TITLES[code] ?? PAYMENT_GENERIC_ERROR_MESSAGE, status)
  }
  return new PaymentApiError(PAYMENT_GENERIC_ERROR_MESSAGE)
}

/**
 * `GET /api/v1/projects/{projectId}/payment-certificates` — the Payment Certificate screen's
 * milestone list (prototype screen #7's left panel, `docs/ECM Planning Prototype.dc.html` line
 * ~314).
 *
 * **This endpoint does not exist on the real backend yet.** `grep "HttpGet" backend/src/CMPlus.WebApi/
 * Controllers/Payment/PaymentCertificatesController.cs` returns zero results — Sprint 9's backend
 * scope (S9-BE-01..06) built the aggregate, the calculator, the ledger and the five *transition*
 * endpoints (submit/approve/return-for-revision/reject/record-payment), but no create or read
 * surface (confirmed by the Sprint 9 security review, L-04: "there is no GET for a certificate at
 * all — nor any create endpoint (`grep "new PaymentCertificate(" in backend/src` returns zero
 * production hits)"). Calling this against the live API returns a 404 today, and — because there is
 * also no create endpoint — it would return an empty list even once implemented, until a
 * certificate-creation flow exists.
 *
 * Still implemented and wired up front-end-side, typed against the exact `PaymentCertificateDto`
 * shape every real S9-BE-05 command already returns, with a correct loading/empty/error state in
 * `PaymentPage`/`usePaymentCertificates` — same discipline as `features/info/api.ts#getProject`'s
 * identical situation (that function's own remarks). Flagged to `backend-developer`/
 * `system-architect` as fast-follow work; see this sprint's frontend report. Do not delete this
 * function or its call site to "fix" the 404 — the correct fix is adding the endpoints, not
 * removing the read.
 */
export async function listPaymentCertificates(projectId: string): Promise<PaymentCertificateDto[]> {
  try {
    const response = await apiClient.get<PaymentCertificateDto[]>(`/projects/${projectId}/payment-certificates`)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/**
 * `GET /api/v1/payment-certificates/{id}` — single-certificate reload, used after a `409
 * concurrent-transition` conflict (or any manual "โหลดใหม่") to re-sync with the server's current
 * row rather than guessing. The interactive submit/approve/return/reject/record-payment flow in
 * `usePaymentCertificateActions.ts` never depends on this call for its *own* success path — every
 * one of those five real commands already returns the fresh `PaymentCertificateDto` as its response
 * body, so the certificate shown after a mutation is always that response, not a re-fetch.
 *
 * **This endpoint does not exist on the real backend yet** — same gap as
 * `listPaymentCertificates` above (L-04). Kept for the same reason `features/info/api.ts#getProject`
 * is kept: it is the natural, obvious shape (every mutation already proves the response DTO is
 * correct) and a real `GetPaymentCertificateQuery` is a trivial fast-follow reusing
 * `PaymentCertificateDto.From` verbatim, not a new design.
 */
export async function getPaymentCertificate(id: string): Promise<PaymentCertificateDto> {
  try {
    const response = await apiClient.get<PaymentCertificateDto>(`/payment-certificates/${id}`)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/**
 * `GET /api/v1/payment-certificates/{id}/approval-actions` — the append-only approval-history
 * ledger for one certificate (design.md §2.3), what `components/ApprovalChainBar.tsx` needs to show
 * "every step (role/approver/time/comment)" (S9-FE-02 DoD).
 *
 * **This endpoint does not exist on the real backend yet.** Same `grep` evidence as
 * `listPaymentCertificates`'s remarks; explicitly named by the Sprint 9 security review (L-04:
 * "Add the read endpoint (tenant-scoped, same 404 discipline) alongside S9-FE-01/02" — and §9.5(ii):
 * "S9-FE-02's chain bar will need this before the quorum feature is usable"). `ApprovalChainBar`
 * degrades gracefully when this 404s today (an honest "รายละเอียดยังไม่พร้อมใช้งาน" note, never a
 * fabricated history) — see that component's own remarks and `useApprovalActions.ts`.
 */
export async function getApprovalActions(paymentCertificateId: string): Promise<ApprovalActionDto[]> {
  try {
    const response = await apiClient.get<ApprovalActionDto[]>(
      `/payment-certificates/${paymentCertificateId}/approval-actions`,
    )
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/** `POST /api/v1/payment-certificates/{id}/submit` (S9-BE-05) — real, live endpoint.
 * `QS,PM,ProjectDirector,Admin` only server-side (`PaymentCertificatesController.CertificateCrudRoles`). */
export async function submitPaymentCertificate(id: string): Promise<PaymentCertificateDto> {
  try {
    const response = await apiClient.post<PaymentCertificateDto>(`/payment-certificates/${id}/submit`)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/** `POST /api/v1/payment-certificates/{id}/approve` (S9-BE-05) — real, live endpoint. Carries **no**
 * static role gate (`PaymentCertificatesController`'s own remarks: authority is resolved entirely
 * from the document's version-pinned chain) — any authenticated tenant user may call it, and the
 * server is always the actual authorization boundary regardless of what this UI hides. `comment` is
 * optional (`ApprovePaymentCertificateCommandValidator`: max length 2000 only). */
export async function approvePaymentCertificate(id: string, comment?: string): Promise<PaymentCertificateDto> {
  try {
    const payload: ApprovalCommentPayload = { comment: comment?.trim() ? comment.trim() : null }
    const response = await apiClient.post<PaymentCertificateDto>(`/payment-certificates/${id}/approve`, payload)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/** `POST /api/v1/payment-certificates/{id}/return-for-revision` (S9-BE-05) — real, live endpoint.
 * `comment` is mandatory server-side (`ReturnPaymentCertificateForRevisionCommandValidator`:
 * `NotEmpty().MaximumLength(2000)`) — `components/ApprovalActionModal.tsx` enforces this
 * client-side too so the rejection is never a surprise round trip. This is the "ตีกลับ" action —
 * **not** `rejectPaymentCertificate` below; it returns the document to `Draft` with
 * `RevisionNo + 1`, it is not terminal (approval-workflow.md §3/§4). */
export async function returnPaymentCertificateForRevision(
  id: string,
  comment: string,
): Promise<PaymentCertificateDto> {
  try {
    const payload: ApprovalCommentPayload = { comment }
    const response = await apiClient.post<PaymentCertificateDto>(
      `/payment-certificates/${id}/return-for-revision`,
      payload,
    )
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/** `POST /api/v1/payment-certificates/{id}/reject` (S9-BE-05) — real, live endpoint. `comment` is
 * mandatory server-side (`RejectPaymentCertificateCommandValidator`). **Terminal and unrecoverable**
 * (approval-workflow.md §3/§4) — distinct from `returnPaymentCertificateForRevision` above; only the
 * final step's approver may call this (`PaymentCertificateNotAuthorizedForApprovalStep` otherwise). */
export async function rejectPaymentCertificate(id: string, comment: string): Promise<PaymentCertificateDto> {
  try {
    const payload: ApprovalCommentPayload = { comment }
    const response = await apiClient.post<PaymentCertificateDto>(`/payment-certificates/${id}/reject`, payload)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}

/** `POST /api/v1/payment-certificates/{id}/record-payment` (S9-BE-05) — real, live endpoint.
 * `QS,PM,ProjectDirector,Admin` only server-side. `reference` mandatory (max 64 chars),
 * `paidAt` mandatory (`RecordPaymentForPaymentCertificateCommandValidator`). */
export async function recordPayment(
  id: string,
  reference: string,
  paidAt: string,
): Promise<PaymentCertificateDto> {
  try {
    const payload: RecordPaymentPayload = { reference, paidAt }
    const response = await apiClient.post<PaymentCertificateDto>(`/payment-certificates/${id}/record-payment`, payload)
    return response.data
  } catch (error) {
    throw toPaymentApiError(error)
  }
}
