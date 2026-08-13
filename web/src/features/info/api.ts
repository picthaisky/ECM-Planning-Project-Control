import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import { translateImportErrorCode } from './errorTranslation'
import type {
  FileImportJob,
  ImportRouteFormat,
  Project,
  ProjectDetail,
  SetEacAdvancedInputsPayload,
  SetEacAdvancedInputsResult,
  UpdateProjectPayload,
} from './types'

/**
 * Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/auth/api.ts`'s `LoginError` pattern. Only thrown for a *request*-level failure
 * (unknown project, unsupported route segment, expired session, network error) — a rejected
 * *file* (size cap, malformed content, relation cycle, XXE) is not one of these: the backend
 * models it as a successful response whose `FileImportJob.status` is `'Failed'` (see
 * `ImportScheduleFileCommandHandler`'s remarks), so callers must still check `status` on the
 * resolved value, not just catch this class.
 */
export class ImportApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ImportApiError'
    this.status = status
  }
}

const GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toImportApiError(error: unknown): ImportApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    // ResultProblemMapper puts the stable error code in `detail` (e.g. "ImportProjectNotFound") —
    // translate that code to Thai; fall back to the English `title` only if `detail` is missing,
    // then to a generic Thai message.
    const message = problem?.detail
      ? translateImportErrorCode(problem.detail)
      : (problem?.title ?? GENERIC_ERROR_MESSAGE)
    return new ImportApiError(message, status)
  }
  return new ImportApiError(GENERIC_ERROR_MESSAGE)
}

/**
 * `POST /api/v1/projects/{id}/import/{xer|mspdi|xlsx}` (S3-BE-04). Always resolves with a
 * `FileImportJob` — even for a rejected file — never throws for that case; see `ImportApiError`'s
 * remarks on the request-vs-file failure distinction.
 */
export async function importFile(
  projectId: string,
  file: File,
  routeFormat: ImportRouteFormat,
): Promise<FileImportJob> {
  const formData = new FormData()
  formData.append('file', file)

  try {
    const response = await apiClient.post<FileImportJob>(
      `/projects/${projectId}/import/${routeFormat}`,
      formData,
    )
    return response.data
  } catch (error) {
    throw toImportApiError(error)
  }
}

/** `GET .../import/jobs/{jobId}` — single job status/detail lookup, used to poll a `Pending` job
 * to a terminal state. */
export async function getImportJob(projectId: string, jobId: string): Promise<FileImportJob> {
  try {
    const response = await apiClient.get<FileImportJob>(
      `/projects/${projectId}/import/jobs/${jobId}`,
    )
    return response.data
  } catch (error) {
    throw toImportApiError(error)
  }
}

/** `GET .../import/jobs` — newest-first job history for the project's import history table. */
export async function getImportJobHistory(
  projectId: string,
  page = 1,
  pageSize = 20,
): Promise<FileImportJob[]> {
  try {
    const response = await apiClient.get<FileImportJob[]>(`/projects/${projectId}/import/jobs`, {
      params: { page, pageSize },
    })
    return response.data
  } catch (error) {
    throw toImportApiError(error)
  }
}

/**
 * Thrown by `getProject`/`updateProject` with a Thai-first message — mirrors `ImportApiError`.
 */
export class ProjectApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ProjectApiError'
    this.status = status
  }
}

const PROJECT_ERROR_TITLES: Record<string, string> = {
  ProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'domain-rule-violation': 'ข้อมูลไม่ผ่านเงื่อนไขของระบบ กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  // Verified against the real, live `PUT /api/v1/projects/{id}` (not a guess): a
  // `UpdateProjectCommandValidator` failure on this Result<T>-shaped command never reaches
  // GlobalExceptionHandler's "validation-error" branch (that only fires for a non-Result-shaped
  // request per its own doc comment) — it becomes a plain `Result.Failure(rawFluentValidationText)`,
  // which `ResultProblemMapper` maps through its "code not in the table" fallback:
  // `type=".../problems/bad-request"`, `detail=<raw English FluentValidation message>`. Mapped here
  // to the same Thai message as 'validation-error' for that reason.
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  ProjectConcurrencyConflict: 'มีการเปลี่ยนแปลงข้อมูลโครงการนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
  'concurrent-transition': 'มีการเปลี่ยนแปลงข้อมูลโครงการนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
}

const PROJECT_GENERIC_ERROR_MESSAGE = 'บันทึกข้อมูลโครงการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toProjectApiError(error: unknown, extraTitles?: Record<string, string>): ProjectApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    // ResultProblemMapper puts the stable error code in `detail` for known Result failures
    // (e.g. "ProjectNotFound"); GlobalExceptionHandler instead puts a generic FluentValidation/
    // DomainException message there and the stable bucket in the URL-shaped `type`
    // (".../problems/validation-error"/".../problems/bad-request") — try the code table against
    // both, in that order.
    const table = extraTitles ? { ...PROJECT_ERROR_TITLES, ...extraTitles } : PROJECT_ERROR_TITLES
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && table[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Deliberately never falls back to `problem.detail`/`problem.title` here — both can be raw,
    // untranslated English server prose (a FluentValidation message, or a bare framework title)
    // for any error code this table does not yet know about, which must never reach a Thai-first
    // UI verbatim (conventions.md's "never leak" spirit, confirmed by a real 400 response during
    // this sprint's live-backend verification). An unmapped code always gets the generic Thai
    // message instead — never English leakage, only ever a slightly less specific Thai headline.
    return new ProjectApiError(table[code] ?? PROJECT_GENERIC_ERROR_MESSAGE, status)
  }
  return new ProjectApiError(PROJECT_GENERIC_ERROR_MESSAGE)
}

/**
 * `GET /api/v1/projects/{projectId}` — the Project Info screen's "view" half (US-4.3/4.4), now
 * also the S14-FE-02 source for the project's current EAC configuration (`ProjectEacConfig`).
 *
 * **This is now a real, live endpoint** (`ProjectsController` `[HttpGet("{projectId}")]` →
 * `GetProjectQuery` → `ProjectDetailDto`; class-level `[Authorize]`, so any authenticated tenant
 * user can view their own tenant's project, and a cross-tenant/unknown id is a bare 404 — integration
 * test `ProjectsControllerTests` covers the 200, the cross-tenant 404, and the unknown-id 404).
 *
 * Its `ProjectDetailDto` is built as **exactly the superset this call needs**: `ProjectDto`'s full
 * editable field set (the `PUT` response shape) **plus** `eacVariantDefault`/`eacManualEtc`/
 * `eacCustomPerformanceFactor`/`eacManualEtcStaleSince`. The `PUT` response's `ProjectDto`
 * deliberately excludes the EAC fields (ADR-0007(c)'s separately-audited Sprint-7 action), so before
 * this endpoint existed there was no way to load the *current* EAC config on a page reload — this
 * read closes that gap, which is why `ProjectDetail = Project & ProjectEacConfig` and why
 * `EacAdvancedInputsCard`'s full-representation `PUT` can now know the current value of the field the
 * user is not editing (see `setEacAdvancedInputs`'s remarks).
 */
export async function getProject(projectId: string): Promise<ProjectDetail> {
  try {
    const response = await apiClient.get<ProjectDetail>(`/projects/${projectId}`)
    return response.data
  } catch (error) {
    throw toProjectApiError(error)
  }
}

/**
 * `PUT /api/v1/projects/{projectId}` (S4-BE-02, US-4.3/4.4) — real, live endpoint. Every
 * `decimal`/`decimal?` field is sent as a JSON string (`DecimalAsStringJsonConverter`, project-wide
 * — see `types.ts`'s remarks); the caller is responsible for having already validated ranges
 * client-side (`ProjectEditForm`) since this call still surfaces the server's own
 * defense-in-depth rejection verbatim via `ProjectApiError` when it disagrees.
 *
 * Returns the backend's real `ProjectDto` shape only (`Project`, not `ProjectDetail`) — this
 * command never touches the EAC config fields (disjoint field set from
 * `SetEacAdvancedInputsCommand`/`SetEacVariantDefaultCommand`), so its response genuinely does not
 * carry them. `useProjectMasterData.save` merges this into the previously-loaded `ProjectDetail`
 * rather than replacing it, precisely so an unrelated edit here (e.g. the project name) can never
 * blank out the EAC fields on screen.
 */
export async function updateProject(projectId: string, payload: UpdateProjectPayload): Promise<Project> {
  try {
    const response = await apiClient.put<Project>(`/projects/${projectId}`, payload)
    return response.data
  } catch (error) {
    throw toProjectApiError(error)
  }
}

const EAC_ADVANCED_INPUTS_ERROR_TITLES: Record<string, string> = {
  // S14-BE-03 (ProjectErrorCodes): the symmetric "cannot clear while active" guard, approached
  // from *this* direction (`Project.SetEacManualEtc`/`SetEacCustomPerformanceFactor`, invoked by
  // `SetEacAdvancedInputsCommandHandler`) — distinct Thai copy from `features/evm/api.ts`'s own
  // mapping of the *same* backend codes, which covers the opposite direction (selecting the variant
  // while its input is unset). Both point the user at the *other* screen, since that is genuinely
  // where the fix lives (S14-FE-02's explicit ask: "the 'where to set it' explanation needs to
  // actually point somewhere useful").
  ProjectEacManualEtcRequiredForBottomUpEtc:
    'ไม่สามารถล้างค่านี้ได้ เนื่องจากตัวแปร EAC เริ่มต้นของโครงการปัจจุบันคือ Bottom-Up ETC ซึ่งใช้ค่านี้อยู่ — ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน แล้วจึงกลับมาล้างค่านี้',
  'eac-manual-etc-required-for-bottom-up-etc':
    'ไม่สามารถล้างค่านี้ได้ เนื่องจากตัวแปร EAC เริ่มต้นของโครงการปัจจุบันคือ Bottom-Up ETC ซึ่งใช้ค่านี้อยู่ — ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน แล้วจึงกลับมาล้างค่านี้',
  ProjectEacCustomPerformanceFactorRequiredForCustomPf:
    'ไม่สามารถล้างค่านี้ได้ เนื่องจากตัวแปร EAC เริ่มต้นของโครงการปัจจุบันคือ Custom PF ซึ่งใช้ค่านี้อยู่ — ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน แล้วจึงกลับมาล้างค่านี้',
  'eac-custom-performance-factor-required-for-custom-pf':
    'ไม่สามารถล้างค่านี้ได้ เนื่องจากตัวแปร EAC เริ่มต้นของโครงการปัจจุบันคือ Custom PF ซึ่งใช้ค่านี้อยู่ — ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน แล้วจึงกลับมาล้างค่านี้',
}

/** Same bodyless-403 shape as `features/evm/api.ts#EAC_DEFAULT_FORBIDDEN_MESSAGE` — PM/QS/Executive
 * only server-side (`ProjectsController.SetEacAdvancedInputs`, ADR-0007(f)). */
const EAC_ADVANCED_INPUTS_FORBIDDEN_MESSAGE = 'คุณไม่มีสิทธิ์ตั้งค่า EAC ขั้นสูงของโครงการนี้'

/**
 * `PUT /api/v1/projects/{projectId}/eac-advanced-inputs` (S14-BE-03) — real, live endpoint. Sets
 * both `Project.EacManualEtc` (the `BottomUpEtc` input) and `Project.EacCustomPerformanceFactor`
 * (the `CustomPf` input) together, **full-representation**: the backend's
 * `SetEacAdvancedInputsCommandHandler` always calls both setters from whatever this payload
 * carries, so a field omitted here is genuinely *cleared*, never "left alone". `EacAdvancedInputsCard`
 * always echoes the other field's last-known value (from `ProjectDetail`/a prior successful save)
 * rather than ever submitting a value it does not actually know, for exactly this reason.
 */
export async function setEacAdvancedInputs(
  projectId: string,
  payload: SetEacAdvancedInputsPayload,
): Promise<SetEacAdvancedInputsResult> {
  try {
    const response = await apiClient.put<SetEacAdvancedInputsResult>(
      `/projects/${projectId}/eac-advanced-inputs`,
      payload,
    )
    return response.data
  } catch (error) {
    if (error instanceof AxiosError && error.response?.status === 403) {
      throw new ProjectApiError(EAC_ADVANCED_INPUTS_FORBIDDEN_MESSAGE, 403)
    }
    throw toProjectApiError(error, EAC_ADVANCED_INPUTS_ERROR_TITLES)
  }
}

/**
 * `GET .../import/xlsx/template` (S3-BE-03 export half) — downloads the blank progress-update
 * workbook and saves it via the browser's normal download flow. Not part of the four DoD steps
 * (upload/progress/results/history) but slotted next to the XLSX upload row since a user cannot
 * usefully use that row without this template first; trivial given the endpoint already exists.
 */
export async function downloadProgressTemplate(projectId: string): Promise<void> {
  try {
    const response = await apiClient.get<Blob>(`/projects/${projectId}/import/xlsx/template`, {
      responseType: 'blob',
    })

    const url = URL.createObjectURL(response.data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'progress-update-template.xlsx'
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  } catch (error) {
    throw toImportApiError(error)
  }
}
