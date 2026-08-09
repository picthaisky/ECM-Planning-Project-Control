import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  EacVariant,
  EvmResponseDto,
  EvmSnapshotDto,
  SetEacVariantDefaultPayload,
  SetEacVariantDefaultResult,
} from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/wbs/api.ts`'s `WbsApiError`/`features/gantt/api.ts`'s `GanttApiError`. */
export class EvmApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'EvmApiError'
    this.status = status
  }
}

const EVM_ERROR_TITLES: Record<string, string> = {
  // `EvmErrorCodes.ProjectNotFound` (backend) — exact-match, same discipline as
  // `WBS_ERROR_TITLES`'s `CpmProjectNotFound` entry.
  EvmProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบโครงการที่ระบุ',

  // S7-BE-05: closing the same dataDate twice.
  EvmSnapshotAlreadyExists: 'ปิดงวดข้อมูล ณ วันที่นี้ไปแล้ว กรุณาเลือกวันที่อื่น',
  'evm-snapshot-already-exists': 'ปิดงวดข้อมูล ณ วันที่นี้ไปแล้ว กรุณาเลือกวันที่อื่น',

  // `GET .../evm/snapshots?from=&to=` with an inverted range.
  EvmInvalidSnapshotRange: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้นต้องไม่มากกว่าวันที่สิ้นสุด)',
  'evm-invalid-snapshot-range': 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้นต้องไม่มากกว่าวันที่สิ้นสุด)',

  // Defensive only — this module never sends an `eacVariant` override itself (design.md §1.1's
  // "no round trip": the selector switches already-fetched variants client-side, see
  // `evmSelectors.ts`), so this code path is not reachable from this app's own UI actions today,
  // but is still mapped for completeness/future-proofing, same discipline as every other
  // `*ApiError` module in this codebase.
  'invalid-eac-variant': 'ตัวแปร EAC ที่ระบุไม่ถูกต้อง',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const EVM_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

/** `EvmErrorCodes.InvalidEacVariantPrefix` (backend) — dynamic-suffix code (mirrors
 * `features/wbs/api.ts`'s `CPM_CYCLE_DETECTED_PREFIX` handling of `CpmCycleDetected`). */
const INVALID_EAC_VARIANT_PREFIX = 'EvmInvalidEacVariant'

/**
 * `PUT .../eac-default` is restricted to PM/QS/Executive at the WebApi `[Authorize(Roles=...)]`
 * boundary (ADR-0007(f)) — a role-based 403 from ASP.NET Core's own authorization middleware here
 * carries **no response body at all** (no custom `IAuthorizationMiddlewareResultHandler`/
 * `OnForbidden` is registered in `Program.cs`, confirmed by reading the real pipeline wiring, not
 * assumed), so this must be special-cased *before* any `problem.detail` lookup — a bodyless 403
 * would otherwise fall through to the generic message anyway, but checking the status first avoids
 * depending on that coincidence and gives a message specific to *why* (permission, not "something
 * went wrong"). The button that calls this is hidden client-side for non-PM/QS/Executive roles
 * (`RequireRole`, `EvmPage.tsx`) — this branch is defense-in-depth only.
 */
const EAC_DEFAULT_FORBIDDEN_MESSAGE = 'คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้'

function toEvmApiError(error: unknown, forbiddenMessage?: string): EvmApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    if (status === 403 && forbiddenMessage) {
      return new EvmApiError(forbiddenMessage, status)
    }

    const problem = error.response?.data as ProblemDetails | undefined

    if (problem?.detail?.startsWith(INVALID_EAC_VARIANT_PREFIX)) {
      return new EvmApiError(EVM_ERROR_TITLES['invalid-eac-variant'], status)
    }

    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && EVM_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/wbs/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new EvmApiError(EVM_ERROR_TITLES[code] ?? EVM_GENERIC_ERROR_MESSAGE, status)
  }
  return new EvmApiError(EVM_GENERIC_ERROR_MESSAGE)
}

export interface GetEvmOptions {
  /** ISO 8601 instant. Omitted defaults server-side to `Project.DataDate` (`GetEvmQuery`'s own
   * remarks) — no date-picker exists in this sprint's UI (S7-FE-01..04's DoD names no such
   * control), so `EvmPage` never passes this; kept on the function signature because the
   * documented query contract supports it (design.md §2.1) and a future sprint's historical-date
   * view is then a caller-only change, not a new API method. */
  dataDate?: string
}

/**
 * `GET /api/v1/projects/{projectId}/evm` (S7-BE-03) — real, live endpoint. Deliberately never
 * passes `?eacVariant=` from this module's callers: the response already carries *every*
 * computable variant (`variants[]`) plus the project's own current default (`selectedVariant`), so
 * the EAC variant switcher is local UI state over this one response — see `evmSelectors.ts` and
 * `EvmPage.tsx`'s remarks (design.md §1.1: "the client switches with no round trip").
 */
export async function getEvm(projectId: string, options?: GetEvmOptions): Promise<EvmResponseDto> {
  try {
    const response = await apiClient.get<EvmResponseDto>(`/projects/${projectId}/evm`, {
      params: { dataDate: options?.dataDate },
    })
    return response.data
  } catch (error) {
    throw toEvmApiError(error)
  }
}

export interface ListEvmSnapshotsOptions {
  /** ISO 8601 instant, inclusive. */
  from?: string
  /** ISO 8601 instant, inclusive. */
  to?: string
}

/**
 * `GET /api/v1/projects/{projectId}/evm/snapshots?from=&to=` (S7-BE-05, added during Sprint 7
 * review specifically for this DoD — see `ListEvmSnapshotsQuery`'s own remarks) — real, live
 * endpoint. Returns closed reporting periods ascending by data date; omitting both bounds returns
 * every closed period for the project. This is what `SCurveChart` sources every point *before* the
 * live data date from (ADR-0009) — never a live recomputation for a past date.
 */
export async function listEvmSnapshots(
  projectId: string,
  options?: ListEvmSnapshotsOptions,
): Promise<EvmSnapshotDto[]> {
  try {
    const response = await apiClient.get<EvmSnapshotDto[]>(`/projects/${projectId}/evm/snapshots`, {
      params: { from: options?.from, to: options?.to },
    })
    return response.data
  } catch (error) {
    throw toEvmApiError(error)
  }
}

/**
 * `PUT /api/v1/projects/{projectId}/eac-default` (S7-BE-04, ADR-0007(f)) — real, live endpoint.
 * PM/QS/Executive only server-side; see `toEvmApiError`'s remarks on the bodyless-403 shape.
 */
export async function setEacVariantDefault(
  projectId: string,
  variant: EacVariant,
): Promise<SetEacVariantDefaultResult> {
  const payload: SetEacVariantDefaultPayload = { variant }
  try {
    const response = await apiClient.put<SetEacVariantDefaultResult>(
      `/projects/${projectId}/eac-default`,
      payload,
    )
    return response.data
  } catch (error) {
    throw toEvmApiError(error, EAC_DEFAULT_FORBIDDEN_MESSAGE)
  }
}
