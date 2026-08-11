import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  ActivateBaselineResultDto,
  BaselineComparisonDto,
  BaselineDto,
  CaptureBaselinePayload,
} from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/evm/api.ts`'s `EvmApiError`/`features/info/api.ts`'s `ProjectApiError`. Also
 * carries the raw backend `code` (`ProblemDetails.detail`, e.g. `"BaselineNoActiveBaseline"`) so a
 * caller that needs to branch on *which* failure this was (`useBaselineComparison.ts`'s
 * `NoActiveBaseline` load state) does so on the stable code, never by pattern-matching the
 * already-localised Thai message text. */
export class BaselineApiError extends Error {
  readonly status?: number
  readonly code?: string

  constructor(message: string, status?: number, code?: string) {
    super(message)
    this.name = 'BaselineApiError'
    this.status = status
    this.code = code
  }
}

/** `BaselineErrorCodes` (backend, via `ResultProblemMapper`), transcribed from the real
 * `ResultProblemMapper.cs` table, not guessed. */
const BASELINE_ERROR_TITLES: Record<string, string> = {
  BaselineProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  BaselineNotFound: 'ไม่พบ Baseline ที่ระบุ',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',
  BaselineNoActiveBaseline: 'โครงการนี้ยังไม่มี Baseline ที่ Active อยู่ กรุณาสร้างและ Activate Baseline ก่อนเปรียบเทียบ',
  'baseline-no-active-baseline':
    'โครงการนี้ยังไม่มี Baseline ที่ Active อยู่ กรุณาสร้างและ Activate Baseline ก่อนเปรียบเทียบ',
  BaselineConcurrencyConflict: 'มีการเปลี่ยนแปลง Baseline นี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'concurrent-transition': 'มีการเปลี่ยนแปลง Baseline นี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const BASELINE_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

/** Bodyless-403 shape — same discipline as `features/evm/api.ts#toEvmApiError`'s remarks: a
 * role-based 403 from ASP.NET Core's own authorization middleware carries no response body at all
 * (no custom `IAuthorizationMiddlewareResultHandler` registered), so it must be special-cased
 * before any `problem.detail` lookup. */
function toBaselineApiError(error: unknown, forbiddenMessage?: string): BaselineApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    if (status === 403 && forbiddenMessage) {
      return new BaselineApiError(forbiddenMessage, status)
    }

    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && BASELINE_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    return new BaselineApiError(BASELINE_ERROR_TITLES[code] ?? BASELINE_GENERIC_ERROR_MESSAGE, status, problem?.detail)
  }
  return new BaselineApiError(BASELINE_GENERIC_ERROR_MESSAGE)
}

/** Stable code for `BaselineErrorCodes.NoActiveBaseline` — `useBaselineComparison.ts` branches on
 * this directly (never on the localised message text). */
export const BASELINE_NO_ACTIVE_BASELINE_CODE = 'BaselineNoActiveBaseline'

const CAPTURE_ACTIVATE_FORBIDDEN_MESSAGE = 'คุณไม่มีสิทธิ์บันทึกหรือเปิดใช้งาน Baseline สำหรับโครงการนี้'
const COMPARE_FORBIDDEN_MESSAGE = 'คุณไม่มีสิทธิ์ดูข้อมูลเปรียบเทียบ Baseline ของโครงการนี้'

/**
 * `GET /api/v1/projects/{projectId}/baselines` — the Baseline screen's "list every captured
 * baseline" need (S14-FE-01 DoD: "จัดการหลายชุด").
 *
 * **This endpoint does not exist on the real backend yet.** `BaselinesController` (real source,
 * read directly — `backend/src/CMPlus.WebApi/Controllers/Baselines/BaselinesController.cs`) wires
 * exactly three actions: `POST` (capture), `POST .../activate`, `GET .../compare`. No `[HttpGet]`
 * with no sub-route exists, and no `ListBaselinesQuery` exists in
 * `CMPlus.Application/Features/Baseline/`. It was not assigned to any Sprint 14 backend row — the
 * capture command already returns the new baseline's full `BaselineDto`, so the backend itself
 * never needed a list read — but the *screen* genuinely does (a QS returning to this page after
 * the session that captured a baseline has ended has no way to see what exists).
 *
 * Calling this against the live API returns a 404 today. It is still implemented and wired up
 * front-end-side, typed against the natural `BaselineDto[]` shape (`BaselineDto.From`'s own field
 * list, unchanged) — same discipline as `features/info/api.ts#getProject`/
 * `features/payment/api.ts#listPaymentCertificates`'s identical situation. `useBaselines.ts`
 * degrades gracefully when this 404s: it falls back to a **session-local** list built from
 * `capture`/`activate` responses (clearly flagged in the UI, `BaselineListPanel.tsx`) rather than
 * blocking the whole screen — `compare` does not depend on this call at all (it defaults
 * server-side to the project's active baseline), so the comparison table stays fully live and
 * correct regardless. Flagged to `backend-developer`/`system-architect` as fast-follow work; the
 * natural fix is a trivial `ListBaselinesQuery` reusing `BaselineDto` verbatim.
 */
export async function listBaselines(projectId: string): Promise<BaselineDto[]> {
  try {
    const response = await apiClient.get<BaselineDto[]>(`/projects/${projectId}/baselines`)
    return response.data
  } catch (error) {
    throw toBaselineApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/baselines` (S14-BE-01) — real, live endpoint. Snapshots
 * every activity's date/duration/budget under a new `Baseline.Id`. PM/Planning/Admin only
 * server-side (`BaselinesController.Capture`). */
export async function captureBaseline(projectId: string, name: string): Promise<BaselineDto> {
  const payload: CaptureBaselinePayload = { name }
  try {
    const response = await apiClient.post<BaselineDto>(`/projects/${projectId}/baselines`, payload)
    return response.data
  } catch (error) {
    throw toBaselineApiError(error, CAPTURE_ACTIVATE_FORBIDDEN_MESSAGE)
  }
}

/** `POST /api/v1/projects/{projectId}/baselines/{baselineId}/activate` (S14-BE-01) — real, live
 * endpoint. Makes `baselineId` the project's sole active baseline (DB-enforced uniqueness).
 * PM/Planning/Admin only server-side, same gate as `captureBaseline`. */
export async function activateBaseline(projectId: string, baselineId: string): Promise<ActivateBaselineResultDto> {
  try {
    const response = await apiClient.post<ActivateBaselineResultDto>(
      `/projects/${projectId}/baselines/${baselineId}/activate`,
    )
    return response.data
  } catch (error) {
    throw toBaselineApiError(error, CAPTURE_ACTIVATE_FORBIDDEN_MESSAGE)
  }
}

export interface CompareBaselineOptions {
  /** Omitted compares against the project's currently-active baseline (the backend's own default —
   * `CompareBaselineQuery`'s remarks: "never lets the comparison target an inactive baseline"
   * implicitly, unless a specific id is supplied). */
  baselineId?: string
}

/** `GET /api/v1/projects/{projectId}/baselines/compare?baselineId=` (S14-BE-02) — real, live
 * endpoint, independent of `listBaselines`'s 404 gap (see that function's own remarks). Returns the
 * per-activity current-vs-baseline delta and the three reproducible summary tiles.
 * PM/QS/ProjectDirector/Executive/Admin only server-side (`BaselinesController.Compare`). */
export async function compareBaseline(
  projectId: string,
  options?: CompareBaselineOptions,
): Promise<BaselineComparisonDto> {
  try {
    const response = await apiClient.get<BaselineComparisonDto>(`/projects/${projectId}/baselines/compare`, {
      params: { baselineId: options?.baselineId },
    })
    return response.data
  } catch (error) {
    throw toBaselineApiError(error, COMPARE_FORBIDDEN_MESSAGE)
  }
}
