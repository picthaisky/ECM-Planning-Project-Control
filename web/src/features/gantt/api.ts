import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { GanttDto } from './types'

/** Thrown by `getGantt` with a Thai-first message — mirrors `WbsApiError`/`ProjectApiError`. */
export class GanttApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'GanttApiError'
    this.status = status
  }
}

const GANTT_ERROR_TITLES: Record<string, string> = {
  // `GanttErrorCodes.ProjectNotFound` (backend) — exact-match, same discipline as
  // `WBS_ERROR_TITLES`'s `CpmProjectNotFound`/`ProgressProjectNotFound` entries.
  GanttProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  // `ResultProblemMapper`'s shared "not-found" type slug — reached if `problem.detail` ever
  // doesn't carry the raw code verbatim (defense in depth, mirrors `toWbsApiError`'s fallback).
  'not-found': 'ไม่พบโครงการที่ระบุ',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const GANTT_GENERIC_ERROR_MESSAGE = 'โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toGanttApiError(error: unknown): GanttApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && GANTT_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/wbs/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new GanttApiError(GANTT_ERROR_TITLES[code] ?? GANTT_GENERIC_ERROR_MESSAGE, status)
  }
  return new GanttApiError(GANTT_GENERIC_ERROR_MESSAGE)
}

/**
 * `GET /api/v1/projects/{projectId}/gantt` (S6-BE-01) — real, live endpoint. `GetGanttQuery` also
 * accepts optional `from`/`to` date-range query params (windowing which activities are returned by
 * planned-span overlap), deliberately not used here: S6-FE-01/02 fetch the whole project's
 * activity list once and zoom/scroll entirely client-side (canvas redraw, no re-fetch per zoom
 * level or scroll position) — re-querying per zoom/scroll would reintroduce exactly the network
 * round-trip latency the virtualized/canvas approach (ADR-0004) exists to avoid. Flagged as a
 * possible future optimization (a truly enormous multi-year schedule) rather than built now,
 * since S4-DB-02's 10,000-activity dataset is the perf target this sprint actually verifies
 * against, and it comes back in one response comfortably.
 */
export async function getGantt(projectId: string): Promise<GanttDto> {
  try {
    const response = await apiClient.get<GanttDto>(`/projects/${projectId}/gantt`)
    return response.data
  } catch (error) {
    throw toGanttApiError(error)
  }
}
