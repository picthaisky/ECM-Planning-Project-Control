import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { DashboardResponseDto } from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/evm/api.ts`'s `EvmApiError`/`features/wbs/api.ts`'s `WbsApiError`. */
export class DashboardApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'DashboardApiError'
    this.status = status
  }
}

const DASHBOARD_ERROR_TITLES: Record<string, string> = {
  // `DashboardErrorCodes.ProjectNotFound` (backend) — exact-match, same discipline as
  // `EVM_ERROR_TITLES`'s `EvmProjectNotFound` entry.
  DashboardProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบโครงการที่ระบุ',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const DASHBOARD_GENERIC_ERROR_MESSAGE = 'โหลดข้อมูล Dashboard ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toDashboardApiError(error: unknown): DashboardApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && DASHBOARD_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/evm/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new DashboardApiError(DASHBOARD_ERROR_TITLES[code] ?? DASHBOARD_GENERIC_ERROR_MESSAGE, status)
  }
  return new DashboardApiError(DASHBOARD_GENERIC_ERROR_MESSAGE)
}

export interface GetDashboardOptions {
  /** ISO 8601 instant. Omitted defaults server-side to `Project.DataDate` (`GetDashboardQuery`'s own
   * remarks). No date-picker exists in this sprint's UI (S8-FE-01's DoD names no such control),
   * mirroring `features/evm/api.ts#getEvm`'s identical `dataDate` remark — kept on the function
   * signature because the documented query contract supports it, so a future historical-date view
   * is a caller-only change. */
  dataDate?: string
}

/**
 * `GET /api/v1/projects/{projectId}/dashboard` (S8-BE-02) — real, live endpoint. Role-gated
 * server-side to `PM,QS,ProjectDirector,Executive,Admin` (ADR-0013, mirrors `EvmController`) — a
 * disallowed role never reaches this call in practice (`DashboardPage` gates the whole screen with
 * `RequireRole` first, see that file's remarks), but a bodyless 403 would still fall through to
 * `DASHBOARD_GENERIC_ERROR_MESSAGE` here as defense-in-depth, same as `features/evm/api.ts#getEvm`.
 */
export async function getDashboard(projectId: string, options?: GetDashboardOptions): Promise<DashboardResponseDto> {
  try {
    const response = await apiClient.get<DashboardResponseDto>(`/projects/${projectId}/dashboard`, {
      params: { dataDate: options?.dataDate },
    })
    return response.data
  } catch (error) {
    throw toDashboardApiError(error)
  }
}
