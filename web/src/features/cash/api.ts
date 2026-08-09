import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { CashFlowResponseDto } from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/evm/api.ts`'s `EvmApiError`/`features/dashboard/api.ts`'s `DashboardApiError`. */
export class CashFlowApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'CashFlowApiError'
    this.status = status
  }
}

const CASH_FLOW_ERROR_TITLES: Record<string, string> = {
  // `CashFlowErrorCodes.ProjectNotFound` (backend) — exact-match, same discipline as
  // `EVM_ERROR_TITLES`'s `EvmProjectNotFound` entry.
  CashFlowProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบโครงการที่ระบุ',
  // `CashFlowErrorCodes.InvalidRange` — `?from=` later than the effective data date.
  CashFlowInvalidRange: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้นต้องไม่มากกว่าวันที่ข้อมูลปัจจุบัน)',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const CASH_FLOW_GENERIC_ERROR_MESSAGE = 'โหลดข้อมูล Cash Flow ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toCashFlowApiError(error: unknown): CashFlowApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && CASH_FLOW_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/evm/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new CashFlowApiError(CASH_FLOW_ERROR_TITLES[code] ?? CASH_FLOW_GENERIC_ERROR_MESSAGE, status)
  }
  return new CashFlowApiError(CASH_FLOW_GENERIC_ERROR_MESSAGE)
}

export interface GetCashFlowOptions {
  /** ISO 8601 instant. Omitted defaults server-side to `Project.DataDate` — also the upper bound of
   * the period-bars window (`GetCashFlowQuery`'s own remarks). No date-picker exists in this
   * sprint's UI, mirroring `features/evm/api.ts#getEvm`'s identical `dataDate` remark. */
  dataDate?: string
  /** ISO 8601 instant. Omitted means "since project inception". No range picker exists in this
   * sprint's UI either — kept on the signature for the same forward-compatibility reason. */
  from?: string
}

/**
 * `GET /api/v1/projects/{projectId}/cash-flow` (S8-BE-01) — real, live endpoint. Role-gated
 * server-side to `PM,QS,ProjectDirector,Executive,Admin` (ADR-0013, mirrors `EvmController`/
 * `DashboardController`) — `CashFlowPage` gates the whole screen with `RequireRole` first (see that
 * file's remarks), so a disallowed role never reaches this call in practice; a bodyless 403 would
 * still fall through to `CASH_FLOW_GENERIC_ERROR_MESSAGE` here as defense-in-depth.
 */
export async function getCashFlow(projectId: string, options?: GetCashFlowOptions): Promise<CashFlowResponseDto> {
  try {
    const response = await apiClient.get<CashFlowResponseDto>(`/projects/${projectId}/cash-flow`, {
      params: { dataDate: options?.dataDate, from: options?.from },
    })
    return response.data
  } catch (error) {
    throw toCashFlowApiError(error)
  }
}
