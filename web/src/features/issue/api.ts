import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { CreateIssuePayload, IssueListResultDto, IssueLogDto } from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/vo/api.ts`'s `VoApiError`. */
export class IssueApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'IssueApiError'
    this.status = status
  }
}

/**
 * `IssueLogErrorCodes` (backend) — transcribed from `ResultProblemMapper.cs`'s `KnownErrors` table.
 * Keyed by both the stable `detail` code and the `type` URL's trailing slug (`toIssueApiError` tries
 * `detail` first, falls back to the `type` slug) — same double-keying discipline as
 * `features/vo/api.ts#VO_ERROR_TITLES`.
 */
const ISSUE_ERROR_TITLES: Record<string, string> = {
  IssueLogProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  IssueLogNotFound: 'ไม่พบรายการปัญหานี้ อาจถูกลบหรือย้ายไปแล้ว',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',

  IssueLogActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'issue-log-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  // domain-rules.md §9.1/fixture W-12a: Closed is terminal, no reopen — see IssueTable's own logic.
  IssueAlreadyClosed: 'ปัญหานี้ปิดแล้ว ไม่สามารถเลื่อนสถานะต่อได้',
  'issue-already-closed': 'ปัญหานี้ปิดแล้ว ไม่สามารถเลื่อนสถานะต่อได้',

  IssueLogConcurrencyConflict: 'มีการเปลี่ยนแปลงรายการนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'concurrent-transition': 'มีการเปลี่ยนแปลงรายการนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const ISSUE_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toIssueApiError(error: unknown): IssueApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && ISSUE_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    return new IssueApiError(ISSUE_ERROR_TITLES[code] ?? ISSUE_GENERIC_ERROR_MESSAGE, status)
  }
  return new IssueApiError(ISSUE_GENERIC_ERROR_MESSAGE)
}

/** `GET /api/v1/projects/{projectId}/issues` (S11-BE-03) — real, live endpoint. Returns
 * `{ items, totalCount, statusCounts }` in one response — the tile counts and the table rows come
 * from the exact same server-side query (`types.ts`'s own remarks on why this screen never derives
 * `statusCounts` from `items` itself). Never 404s on an unknown/cross-tenant project id
 * (`ListIssuesQueryHandler`'s own remarks: returns an all-zero result instead). */
export async function listIssues(projectId: string): Promise<IssueListResultDto> {
  try {
    const response = await apiClient.get<IssueListResultDto>(`/projects/${projectId}/issues`)
    return response.data
  } catch (error) {
    throw toIssueApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/issues` (S11-BE-03) — real, live endpoint. Always created
 * `Status = Open` server-side. The response's `sequenceNo` is `null` (see `types.ts`'s remarks) —
 * callers should reload the list rather than trust it for display. */
export async function createIssue(projectId: string, payload: CreateIssuePayload): Promise<IssueLogDto> {
  try {
    const response = await apiClient.post<IssueLogDto>(`/projects/${projectId}/issues`, payload)
    return response.data
  } catch (error) {
    throw toIssueApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/issues/{issueId}/advance-status` (S11-BE-03) — real, live
 * endpoint. Advances exactly one rung (`Open -> Doing` or `Doing -> Closed`); skipping straight to
 * `Closed` is not permitted server-side. No request body — `ClosedAt`/`StartedAt` are always the
 * server clock, never client-supplied (domain-rules.md §9.2). */
export async function advanceIssueStatus(projectId: string, issueId: string): Promise<IssueLogDto> {
  try {
    const response = await apiClient.post<IssueLogDto>(`/projects/${projectId}/issues/${issueId}/advance-status`)
    return response.data
  } catch (error) {
    throw toIssueApiError(error)
  }
}
