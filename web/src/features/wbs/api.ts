import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  ActivityForProgress,
  BatchProgressRequest,
  BatchProgressResult,
  RecalculateCpmResult,
  WbsTreeDto,
} from './types'

/** Thrown by every function in this module with a Thai-first message — mirrors
 * `features/info/api.ts`'s `ProjectApiError`. */
export class WbsApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'WbsApiError'
    this.status = status
  }
}

const WBS_ERROR_TITLES: Record<string, string> = {
  WbsProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  ProgressProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  ProgressUnknownActivity: 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  // Verified against the real, live `POST .../progress/batch` (not a guess): a
  // `BatchRecordProgressCommandValidator` failure (e.g. a % outside 0-100) never reaches
  // GlobalExceptionHandler's "validation-error" branch on this Result<T>-shaped command — it
  // becomes a plain `Result.Failure(rawFluentValidationText)`, which `ResultProblemMapper` maps
  // through its "code not in the table" fallback: `type=".../problems/bad-request"`,
  // `detail=<raw English FluentValidation message>`. See `features/info/api.ts`'s identical fix.
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',

  // S5-BE-04 (RecalculateCpmCommand): CpmProjectNotFound is an exact-match code just like the WBS/
  // Progress "project not found" codes above. CpmDuplicateRelation/CpmUnknownActivityInRelation are
  // also exact-match (no dynamic suffix) — only CpmCycleDetected carries one, handled separately
  // below (its detail *is* the offending chain, e.g. "CpmCycleDetected: A -> B -> A").
  CpmProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  CpmDuplicateRelation:
    'พบความสัมพันธ์ระหว่างกิจกรรมคู่เดียวกันซ้ำกันมากกว่าหนึ่งรายการ กรุณาตรวจสอบข้อมูล Activity Relation',
  CpmUnknownActivityInRelation:
    'พบความสัมพันธ์ที่อ้างอิงถึงกิจกรรมนอกโครงการนี้ กรุณาตรวจสอบข้อมูล Activity Relation',
  // Safety net only — `toWbsApiError`'s dedicated `CPM_CYCLE_DETECTED_PREFIX` branch below always
  // catches a real cycle rejection first (via `problem.detail`) since that is always present on
  // this Result<T>-shaped failure; this covers the (currently theoretical) case where a proxy/
  // gateway strips `detail` but preserves `type`.
  'cpm-cycle-detected': 'พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle) ไม่สามารถคำนวณตารางเวลาได้',
}

const WBS_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

/** `CpmValidationErrorCodes.CycleDetected` (backend) — the stable prefix of a dynamic
 * `Result<T>.Failure` message: `"CpmCycleDetected: A -> B -> A"`. `ResultProblemMapper` copies that
 * whole string into `ProblemDetails.Detail` verbatim (see its own remarks), so it can never be an
 * exact `WBS_ERROR_TITLES` key — matched by prefix instead, same discipline as
 * `features/info/errorTranslation.ts`'s identical `ImportRelationCycleDetected` handling. */
const CPM_CYCLE_DETECTED_PREFIX = 'CpmCycleDetected'

function toWbsApiError(error: unknown): WbsApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined

    // S5-BE-03/S5-BE-04: show the actual offending chain (in Thai, arrows for readability) rather
    // than discard it for a generic message — mirrors `parseImportErrorJson`'s `extractLocation`
    // treatment of `ImportRelationCycleDetected` ("the detail *is* the chain, with no other prose").
    if (problem?.detail?.startsWith(CPM_CYCLE_DETECTED_PREFIX)) {
      const chain = problem.detail.slice(CPM_CYCLE_DETECTED_PREFIX.length).replace(/^:\s*/, '')
      const chainSuffix = chain ? ` (เส้นทางวนซ้ำ: ${chain.replace(/->/g, '→')})` : ''
      return new WbsApiError(
        `พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle) ไม่สามารถคำนวณตารางเวลาได้${chainSuffix}`,
        status,
      )
    }

    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && WBS_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/info/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new WbsApiError(WBS_ERROR_TITLES[code] ?? WBS_GENERIC_ERROR_MESSAGE, status)
  }
  return new WbsApiError(WBS_GENERIC_ERROR_MESSAGE)
}

/** `GET /api/v1/projects/{projectId}/wbs-tree` (S4-BE-01) — real, live endpoint. Node-level rollup
 * only (`activityCount`, never the activity list) by design — see `WbsTreeDto`'s doc comment. */
export async function getWbsTree(projectId: string): Promise<WbsTreeDto> {
  try {
    const response = await apiClient.get<WbsTreeDto>(`/projects/${projectId}/wbs-tree`)
    return response.data
  } catch (error) {
    throw toWbsApiError(error)
  }
}

/**
 * `GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities` — the "which activities does
 * this node contain, and what is each one's current %" read the batch-progress grid needs to
 * populate its rows from a WBS node the user actually browsed to.
 *
 * This endpoint does **not exist on the real backend yet**. `GetWbsTreeQuery` (S4-BE-01)
 * deliberately returns only a per-node `ActivityCount` rollup — "lazy/paginated child-loading" is
 * named directly in that query's own DoD text, but the paginated child endpoint itself was not
 * shipped this sprint, and no other endpoint enumerates `Activity` rows at all (confirmed: no
 * `ActivitiesController`/`IActivityReader` exists anywhere in `backend/src`). Calling this against
 * the live API returns a 404 today.
 *
 * `ProgressUpdatePanel` still handles that failure with a real, honest empty/error state (never a
 * silent hang) and offers a manual "+ เพิ่มกิจกรรมด้วยรหัส (Activity ID)" add-row path
 * (`ManualActivityRow`) so the batch-submit mechanism itself — validation, decrease-confirmation,
 * the real `POST .../progress/batch` call — is fully usable and end-to-end-verifiable today even
 * before this read endpoint ships. Flagged to `backend-developer`/`system-architect` as
 * fast-follow work (a `GetNodeActivitiesQuery` alongside `GetWbsTreeQuery`); see this sprint's
 * frontend report.
 */
export async function getNodeActivities(
  projectId: string,
  wbsNodeId: string,
): Promise<ActivityForProgress[]> {
  try {
    const response = await apiClient.get<ActivityForProgress[]>(
      `/projects/${projectId}/wbs-nodes/${wbsNodeId}/activities`,
    )
    return response.data
  } catch (error) {
    throw toWbsApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/progress/batch` (S4-BE-03) — real, live endpoint. All-or-
 * nothing: either every entry is recorded (`entriesRecorded === entries.length`) or the whole
 * request fails with a `WbsApiError` and nothing is written server-side. */
export async function batchRecordProgress(
  projectId: string,
  request: BatchProgressRequest,
): Promise<BatchProgressResult> {
  try {
    const response = await apiClient.post<BatchProgressResult>(
      `/projects/${projectId}/progress/batch`,
      request,
    )
    return response.data
  } catch (error) {
    throw toWbsApiError(error)
  }
}

/**
 * `POST /api/v1/projects/{projectId}/cpm/recalculate` (S5-BE-04, `RecalculateCpmCommand`) — real,
 * live endpoint (`CpmController`). All-or-nothing per that handler's own remarks: a rejected graph
 * (cycle/duplicate-relation/unknown-activity-in-relation) never mutates a single `Activity`, so a
 * thrown `WbsApiError` here means nothing was written server-side either. `criticalPath` in the
 * response is every critical activity id, already in the graph's own topological (schedule) order.
 */
export async function recalculateCpm(projectId: string): Promise<RecalculateCpmResult> {
  try {
    const response = await apiClient.post<RecalculateCpmResult>(`/projects/${projectId}/cpm/recalculate`)
    return response.data
  } catch (error) {
    throw toWbsApiError(error)
  }
}
