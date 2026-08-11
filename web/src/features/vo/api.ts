import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  ApprovalActionDto,
  ApprovalCommentPayload,
  CreateVariationOrderPayload,
  UpdateVariationOrderContentPayload,
  VariationOrderDto,
} from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/payment/api.ts`'s `PaymentApiError`. */
export class VoApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'VoApiError'
    this.status = status
  }
}

/**
 * `VariationOrderErrorCodes` (backend) + the reused Sprint 2 `ApprovalErrorCodes.PolicyGap`/
 * `ContractValueNotConfigured` — transcribed from `ResultProblemMapper.cs`'s `KnownErrors` table,
 * not guessed. Keyed by both the stable `detail` code and the `type` URL's trailing slug, same
 * double-keying discipline as `features/payment/api.ts#PAYMENT_ERROR_TITLES`, since `toVoApiError`
 * below tries `detail` first and falls back to the `type` slug.
 *
 * Several backend codes deliberately reuse a `type` slug `PaymentApprovalErrorCodes` already
 * defines for the same meaning (`VariationOrderErrorCodes`'s own doc comments call this out) — the
 * Thai text below is worded for a variation order specifically, never copy-pasted from the payment
 * feature's certificate-flavoured copy.
 */
const VO_ERROR_TITLES: Record<string, string> = {
  VariationOrderNotFound: 'ไม่พบ Variation Order ที่ระบุ',
  VariationOrderProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',

  VariationOrderActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'variation-order-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  VariationOrderDuplicateVoNumber: 'เลขที่ VO นี้ถูกใช้แล้วในโครงการนี้ กรุณาระบุเลขที่อื่น',
  'duplicate-vo-number': 'เลขที่ VO นี้ถูกใช้แล้วในโครงการนี้ กรุณาระบุเลขที่อื่น',

  VariationOrderUnknownActivity: 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง',
  'variation-order-unknown-activity': 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง',

  VoScopeBudgetMismatch: 'ผลรวมมูลค่างานที่ปรับ (Scope) ต้องเท่ากับมูลค่า VO พอดี กรุณาตรวจสอบตัวเลข',
  'vo-scope-budget-mismatch': 'ผลรวมมูลค่างานที่ปรับ (Scope) ต้องเท่ากับมูลค่า VO พอดี กรุณาตรวจสอบตัวเลข',

  EmptyVariation: 'ต้องระบุมูลค่า, ผลกระทบต่อระยะเวลา หรือรายการ Scope อย่างน้อยหนึ่งอย่าง',
  'empty-variation': 'ต้องระบุมูลค่า, ผลกระทบต่อระยะเวลา หรือรายการ Scope อย่างน้อยหนึ่งอย่าง',

  // WithdrawAfterVoteCast shares the "document-immutable" `type` slug with
  // InvalidStatusForTransition but carries its own specific `detail` code — keyed here so its more
  // precise Thai message wins over the generic fallback below (`toVoApiError` tries `detail` first).
  VariationOrderWithdrawAfterVoteCast: 'มีการลงมติอนุมัติ/ปฏิเสธในรอบนี้ไปแล้วอย่างน้อยหนึ่งครั้ง ไม่สามารถถอนคำขอได้อีก',

  VariationOrderInvalidStatusForTransition:
    'เอกสารไม่อยู่ในสถานะที่ทำรายการนี้ได้ กรุณาโหลดข้อมูลล่าสุดแล้วลองใหม่',
  'document-immutable': 'เอกสารไม่อยู่ในสถานะที่ทำรายการนี้ได้ กรุณาโหลดข้อมูลล่าสุดแล้วลองใหม่',

  VariationOrderNotAuthorizedForApprovalStep: 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',
  'not-current-step': 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',

  VariationOrderSelfApprovalNotPermitted: 'ผู้สร้างหรือผู้ยื่นเอกสารไม่สามารถอนุมัติเอกสารฉบับของตนเองได้',
  'self-approval-not-permitted': 'ผู้สร้างหรือผู้ยื่นเอกสารไม่สามารถอนุมัติเอกสารฉบับของตนเองได้',

  // ADR-0016: no actor may cast both an approval and a rejection on the same revision.
  VariationOrderDuplicateChainVoter: 'คุณได้อนุมัติหรือปฏิเสธขั้นตอนอื่นของเอกสารฉบับนี้ไปแล้ว ไม่สามารถลงมติซ้ำได้',
  'duplicate-chain-voter': 'คุณได้อนุมัติหรือปฏิเสธขั้นตอนอื่นของเอกสารฉบับนี้ไปแล้ว ไม่สามารถลงมติซ้ำได้',

  VariationOrderConcurrencyConflict: 'มีการเปลี่ยนแปลงเอกสารนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'concurrent-transition': 'มีการเปลี่ยนแปลงเอกสารนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',

  VariationOrderCorruptApprovalChain: 'ไม่สามารถประมวลผลลำดับการอนุมัติของเอกสารนี้ได้ กรุณาติดต่อผู้ดูแลระบบ',
  'corrupt-approval-chain': 'ไม่สามารถประมวลผลลำดับการอนุมัติของเอกสารนี้ได้ กรุณาติดต่อผู้ดูแลระบบ',

  VoWouldMakeContractValueNegative: 'VO นี้จะทำให้ BAC หรือมูลค่าสัญญาต่ำกว่าศูนย์ ไม่สามารถอนุมัติได้',
  'vo-would-make-contract-value-negative': 'VO นี้จะทำให้ BAC หรือมูลค่าสัญญาต่ำกว่าศูนย์ ไม่สามารถอนุมัติได้',

  VoOmitsExecutedScope: 'มีรายการลดงบประมาณของกิจกรรมที่มีความคืบหน้าแล้ว ไม่สามารถอนุมัติได้ กรุณาตรวจสอบ Scope',
  'vo-omits-executed-scope': 'มีรายการลดงบประมาณของกิจกรรมที่มีความคืบหน้าแล้ว ไม่สามารถอนุมัติได้ กรุณาตรวจสอบ Scope',

  VoScopeActivityBudgetWouldBeNegative: 'รายการปรับงบประมาณจะทำให้งบของกิจกรรมนั้นต่ำกว่าศูนย์ กรุณาตรวจสอบ Scope',
  'vo-scope-activity-budget-would-be-negative':
    'รายการปรับงบประมาณจะทำให้งบของกิจกรรมนั้นต่ำกว่าศูนย์ กรุณาตรวจสอบ Scope',

  VoEscalationThresholdCrossedSinceSubmission:
    'มูลค่า VO สะสมของโครงการข้ามเกณฑ์ escalation นับตั้งแต่ส่งเอกสารนี้ กรุณา "ตีกลับแก้ไข" แล้วส่งใหม่เพื่อให้สายอนุมัติรวมผู้อนุมัติที่กำหนดไว้',
  'vo-escalation-threshold-crossed-since-submission':
    'มูลค่า VO สะสมของโครงการข้ามเกณฑ์ escalation นับตั้งแต่ส่งเอกสารนี้ กรุณา "ตีกลับแก้ไข" แล้วส่งใหม่เพื่อให้สายอนุมัติรวมผู้อนุมัติที่กำหนดไว้',

  // Sprint 2 ApprovalErrorCodes, reused as-is by Submit (fail-closed) — same codes/copy as
  // features/payment/api.ts#PAYMENT_ERROR_TITLES for the identical meaning.
  ApprovalPolicyGap: 'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',
  'approval-policy-gap':
    'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',
  ContractValueNotConfigured: 'ยังไม่ได้ตั้งค่ามูลค่าสัญญาฐานของโครงการ ไม่สามารถประเมินเกณฑ์ escalation ได้',
  'contract-value-not-configured': 'ยังไม่ได้ตั้งค่ามูลค่าสัญญาฐานของโครงการ ไม่สามารถประเมินเกณฑ์ escalation ได้',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const VO_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toVoApiError(error: unknown): VoApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && VO_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/payment/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new VoApiError(VO_ERROR_TITLES[code] ?? VO_GENERIC_ERROR_MESSAGE, status)
  }
  return new VoApiError(VO_GENERIC_ERROR_MESSAGE)
}

/** `GET /api/v1/projects/{projectId}/variation-orders` (S10-BE-01/02, `ListVariationOrdersQuery`) —
 * real, live endpoint (`ProjectVariationOrdersController.List`). Never 404s on an unknown/cross-
 * tenant project id — returns an empty list instead (that handler's own remarks). */
export async function listVariationOrders(projectId: string): Promise<VariationOrderDto[]> {
  try {
    const response = await apiClient.get<VariationOrderDto[]>(`/projects/${projectId}/variation-orders`)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `GET /api/v1/variation-orders/{id}` — real, live endpoint (`VariationOrdersController.GetById`).
 * `PM,Planning,QS,ProjectDirector,Admin` only server-side (`VoCrudRoles`) — notably **not**
 * `Executive`, even though an escalated chain's final step may require an Executive to approve it
 * (see this feature's frontend report: a real, discovered authorization gap, not a frontend
 * assumption). */
export async function getVariationOrder(id: string): Promise<VariationOrderDto> {
  try {
    const response = await apiClient.get<VariationOrderDto>(`/variation-orders/${id}`)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/**
 * `GET /api/v1/variation-orders/{id}/approval-actions` — the append-only approval-history ledger
 * for one VO (`GetApprovalActionHistoryQuery`, shared unchanged with Payment Certificate per that
 * query's own doc comment).
 *
 * **This 404s unconditionally on the real backend today**, even for a VO that genuinely exists and
 * belongs to the caller's tenant — verified from source, not assumed:
 * `GetApprovalActionHistoryQueryHandler.DocumentExistsAsync`'s `switch` only has a case arm for
 * `ApprovalDocumentType.PaymentCertificate` (querying `IPaymentCertificateRepository`); the
 * `VariationOrder` arm falls through to `_ => false`, and the handler's constructor never even
 * injects an `IVariationOrderRepository` to check against. The controller action and the DTO shape
 * are both real and already wired correctly — only that one `switch` arm was never added, despite
 * the query's own doc comment saying Sprint 10 would add it ("not a redesign"). Flagged to
 * `backend-developer` as a one-line fix (the same fix shape `PaymentCertificate` already has), not a
 * new design. `useVoApprovalActions.ts` degrades this exactly like `features/payment/
 * useApprovalActions.ts` degrades its own (differently-caused) 404 — an honest `'unavailable'`
 * state, never fabricated history.
 */
export async function getVoApprovalActions(variationOrderId: string): Promise<ApprovalActionDto[]> {
  try {
    const response = await apiClient.get<ApprovalActionDto[]>(
      `/variation-orders/${variationOrderId}/approval-actions`,
    )
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/variation-orders` (S10-BE-01, `CreateVariationOrderCommand`)
 * — real, live endpoint. `PM,Planning,QS,ProjectDirector,Admin` only server-side. */
export async function createVariationOrder(
  projectId: string,
  payload: CreateVariationOrderPayload,
): Promise<VariationOrderDto> {
  try {
    const response = await apiClient.post<VariationOrderDto>(`/projects/${projectId}/variation-orders`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `PUT /api/v1/variation-orders/{id}/content` — real, live endpoint. Only accepted while
 * `Status === Draft` (never-submitted, or returned-for-revision) — server-enforced via
 * `SetVariationContent`'s `EnsureContentEditable` guard (domain-rules.md §2.4). */
export async function updateVariationOrderContent(
  id: string,
  payload: UpdateVariationOrderContentPayload,
): Promise<VariationOrderDto> {
  try {
    const response = await apiClient.put<VariationOrderDto>(`/variation-orders/${id}/content`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/submit` — real, live endpoint. Resolves and snapshots the
 * approval chain server-side (domain-rules.md §2.3/§3), including the cumulative-VO-escalation rung
 * when it applies (§4). */
export async function submitVariationOrder(id: string): Promise<VariationOrderDto> {
  try {
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/submit`)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/approve` — real, live endpoint. Carries **no** static role
 * gate (`VariationOrdersController`'s own remarks: authority is resolved entirely from the
 * document's version-pinned chain, which may legitimately require `Executive` at an escalated final
 * step). `comment` is optional. */
export async function approveVariationOrder(id: string, comment?: string): Promise<VariationOrderDto> {
  try {
    const payload: ApprovalCommentPayload = { comment: comment?.trim() ? comment.trim() : null }
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/approve`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/return-for-revision` — real, live endpoint. `comment` is
 * mandatory server-side. This is the "ตีกลับ" action — **not** `rejectVariationOrder` below; it
 * returns the document to `Draft` with `RevisionNo + 1` and every collected vote voided, it is not
 * terminal, and the document **can be resubmitted** (domain-rules.md §1/§2 — the exact trap the
 * prototype's own VO table badge fell into, `voStatusLabels.ts`'s remarks). */
export async function returnVariationOrderForRevision(id: string, comment: string): Promise<VariationOrderDto> {
  try {
    const payload: ApprovalCommentPayload = { comment }
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/return-for-revision`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/reject` — real, live endpoint. `comment` is mandatory
 * server-side. **Terminal** once the reject-quorum is satisfied — but per ADR-0016, quorum now
 * binds rejection exactly as it binds approval, so a single reject on a `QuorumCount > 1` step does
 * **not** immediately terminate the document; see `useVariationOrderActions.ts`'s quorum-diff
 * handling, mirroring the same before/after-unchanged heuristic Sprint 9 used for approval. */
export async function rejectVariationOrder(id: string, comment: string): Promise<VariationOrderDto> {
  try {
    const payload: ApprovalCommentPayload = { comment }
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/reject`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/withdraw` — real, live endpoint (domain-rules.md §2.3 notes
 * Sprint 10 must expose this, unlike Sprint 9's still-unreachable IPC pair, N-02). Submitter only,
 * and only before any vote has been cast this revision. */
export async function withdrawVariationOrder(id: string): Promise<VariationOrderDto> {
  try {
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/withdraw`)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}

/** `POST /api/v1/variation-orders/{id}/cancel` — real, live endpoint. `Draft` only; creator or PM;
 * `comment` mandatory ("a cancelled instruction is itself evidence", domain-rules.md §2.3). */
export async function cancelVariationOrder(id: string, comment: string): Promise<VariationOrderDto> {
  try {
    const payload: ApprovalCommentPayload = { comment }
    const response = await apiClient.post<VariationOrderDto>(`/variation-orders/${id}/cancel`, payload)
    return response.data
  } catch (error) {
    throw toVoApiError(error)
  }
}
