import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  EotEvaluationDto,
  EvaluateEotPayload,
  RecordWeatherLogCorrectionPayload,
  RecordWeatherLogPayload,
  WeatherLogDto,
} from './types'

/** Thrown by every function in this module with a Thai-first message ready to render directly —
 * mirrors `features/vo/api.ts`'s `VoApiError`. Shared by both the weather-log write paths
 * (`WeatherLogErrorCodes`) and the EOT evaluator (`EotErrorCodes`) — one feature folder, one error
 * type, same discipline `features/vo/api.ts` uses for its own reused Sprint-2 approval codes. */
export class WeatherApiError extends Error {
  readonly status?: number
  /** Raw backend `ProblemDetails.detail`/type-slug code behind the already-Thai `.message` — S13-FE-01:
   * `weatherOutbox.ts`'s uploaders check this against `IDEMPOTENCY_CONFLICT_CODES` to decide
   * `OutboxConflictError` vs an ordinary retryable throw. Mirrors `features/photo/api.ts#PhotoApiError.code`
   * exactly. `undefined` for a non-Axios error. */
  readonly code?: string

  constructor(message: string, status?: number, code?: string) {
    super(message)
    this.name = 'WeatherApiError'
    this.status = status
    this.code = code
  }
}

/**
 * `WeatherLogErrorCodes` + `EotErrorCodes` (backend) — transcribed from `ResultProblemMapper.cs`'s
 * `KnownErrors` table, not guessed. Keyed by both the stable `detail` code and the `type` URL's
 * trailing slug (`toWeatherApiError` tries `detail` first, falls back to the `type` slug) — same
 * double-keying discipline as `features/vo/api.ts#VO_ERROR_TITLES`.
 */
const WEATHER_ERROR_TITLES: Record<string, string> = {
  WeatherLogProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',

  WeatherLogActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'weather-log-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  WeatherLogUnknownActivity: 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรหัสกิจกรรมที่ระบุอีกครั้ง',
  'weather-log-unknown-activity': 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรหัสกิจกรรมที่ระบุอีกครั้ง',

  WeatherLogInvalidDateRange: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',
  'weather-log-invalid-date-range': 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',

  // §8.2 chain-integrity rule 1: CorrectsWeatherLogId (from the route) does not resolve in this project.
  WeatherLogCorrectionTargetNotFound: 'ไม่พบบันทึกสภาพอากาศต้นฉบับที่ต้องการแก้ไขในโครงการนี้',
  'weather-log-correction-target-not-found': 'ไม่พบบันทึกสภาพอากาศต้นฉบับที่ต้องการแก้ไขในโครงการนี้',

  // §8.2 chain-integrity rule 2 (load-bearing): a correction must target the current chain tail.
  // The UI reloads the list on this error so the stale "which row is correctable" state clears.
  WeatherLogAlreadySuperseded:
    'บันทึกนี้มีรายการแก้ไขอื่นอยู่แล้ว กรุณาโหลดข้อมูลใหม่แล้วแก้ไขจากรายการล่าสุดของสายการแก้ไขนี้แทน',
  'weather-log-already-superseded':
    'บันทึกนี้มีรายการแก้ไขอื่นอยู่แล้ว กรุณาโหลดข้อมูลใหม่แล้วแก้ไขจากรายการล่าสุดของสายการแก้ไขนี้แทน',

  WeatherLogCorrectionOrdering: 'ไม่สามารถบันทึกรายการแก้ไขได้ (ลำดับเวลาของรายการไม่ถูกต้อง)',
  'weather-log-correction-ordering': 'ไม่สามารถบันทึกรายการแก้ไขได้ (ลำดับเวลาของรายการไม่ถูกต้อง)',

  // §8.3's deliberate 405 — this UI never calls PUT/PATCH/DELETE itself, kept only as a safety net.
  WeatherLogIsImmutable: 'ไม่สามารถแก้ไขหรือลบบันทึกสภาพอากาศได้ กรุณาบันทึกรายการแก้ไขใหม่แทน',
  'weather-log-is-immutable': 'ไม่สามารถแก้ไขหรือลบบันทึกสภาพอากาศได้ กรุณาบันทึกรายการแก้ไขใหม่แทน',

  // S11-BE-02: EvaluateEotCommand.
  EotProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  EotActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'eot-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  // §3.3: "no default calendar ⟹ block, never guess".
  EotProjectCalendarNotConfigured: 'โครงการยังไม่ได้ตั้งค่าปฏิทินการทำงานหลัก (Calendar) จึงไม่สามารถประเมิน EOT ได้',
  'eot-project-calendar-not-configured':
    'โครงการยังไม่ได้ตั้งค่าปฏิทินการทำงานหลัก (Calendar) จึงไม่สามารถประเมิน EOT ได้',

  // §4.4: never a single CpmRun captured — the contemporaneous ruling is unanswerable even degraded.
  EotNoCpmRunAvailable: 'โครงการยังไม่มีประวัติการคำนวณ CPM จึงไม่สามารถประเมิน EOT ได้ กรุณาคำนวณ CPM ก่อน (หน้า Gantt / CPM)',
  'eot-no-cpm-run-available':
    'โครงการยังไม่มีประวัติการคำนวณ CPM จึงไม่สามารถประเมิน EOT ได้ กรุณาคำนวณ CPM ก่อน (หน้า Gantt / CPM)',

  EotInvalidWindowRange: 'ช่วงวันที่ประเมินไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',
  'eot-invalid-window-range': 'ช่วงวันที่ประเมินไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',

  EotCpmRunDataCorrupt: 'ข้อมูลผลการคำนวณ CPM ที่บันทึกไว้ไม่สอดคล้องกัน กรุณาติดต่อผู้ดูแลระบบ',
  'eot-cpm-run-data-corrupt': 'ข้อมูลผลการคำนวณ CPM ที่บันทึกไว้ไม่สอดคล้องกัน กรุณาติดต่อผู้ดูแลระบบ',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',

  // S13-BE-01 `IdempotencyMiddleware` wraps both weather-log write actions
  // (`[Idempotent]` on `ProjectWeatherLogsController.Record`/`RecordCorrection`) — see
  // `services/outbox/errors.ts`'s remarks on which of these are a terminal outbox conflict versus an
  // ordinary retryable failure.
  IdempotencyPayloadMismatch:
    'รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้ (คีย์ซ้ำแต่เนื้อหาต่างกัน) ระบบจะไม่ลองส่งซ้ำให้อัตโนมัติอีก กรุณาตรวจสอบรายการนี้',
  'idempotency-payload-mismatch':
    'รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้ (คีย์ซ้ำแต่เนื้อหาต่างกัน) ระบบจะไม่ลองส่งซ้ำให้อัตโนมัติอีก กรุณาตรวจสอบรายการนี้',
  IdempotencyRequestInProgress: 'มีการส่งรายการนี้อยู่แล้วจากอีกอุปกรณ์/แท็บในขณะนี้ กรุณารอสักครู่แล้วลองใหม่',
  'idempotency-request-in-progress': 'มีการส่งรายการนี้อยู่แล้วจากอีกอุปกรณ์/แท็บในขณะนี้ กรุณารอสักครู่แล้วลองใหม่',
  IdempotencyResponseNotReplayable:
    'ไม่สามารถยืนยันผลของรายการนี้ได้ ระบบจะไม่ลองส่งซ้ำให้อัตโนมัติอีก กรุณาตรวจสอบข้อมูลบนระบบก่อนบันทึกใหม่',
  'idempotency-response-not-replayable':
    'ไม่สามารถยืนยันผลของรายการนี้ได้ ระบบจะไม่ลองส่งซ้ำให้อัตโนมัติอีก กรุณาตรวจสอบข้อมูลบนระบบก่อนบันทึกใหม่',
  IdempotencyActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'idempotency-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
}

const WEATHER_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toWeatherApiError(error: unknown): WeatherApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && WEATHER_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    // Never falls back to raw `problem.detail`/`problem.title` — see `features/vo/api.ts`'s
    // identical comment; an unmapped code always gets the generic Thai message instead.
    return new WeatherApiError(WEATHER_ERROR_TITLES[code] ?? WEATHER_GENERIC_ERROR_MESSAGE, status, code || undefined)
  }
  return new WeatherApiError(WEATHER_GENERIC_ERROR_MESSAGE)
}

export interface ListWeatherLogsOptions {
  /** ISO 8601 instant, inclusive, against `LogDate` (S11-DB-01's date-range-as-seek DoD). */
  from?: string
  /** ISO 8601 instant, inclusive. */
  to?: string
}

/** `GET /api/v1/projects/{projectId}/weather-logs?from=&to=` (S11-BE-01) — real, live endpoint.
 * Returns the **full correction history** (every `Original`/`Correction`/`Retraction` row, newest
 * `logDate` first) — never pre-filtered to the effective set; see `weatherChain.ts`. Omitting both
 * bounds returns the whole project history. Never 404s on an unknown/cross-tenant project id
 * (`ListWeatherLogsQueryHandler`'s own remarks: returns an empty list instead). */
export async function listWeatherLogs(
  projectId: string,
  options?: ListWeatherLogsOptions,
): Promise<WeatherLogDto[]> {
  try {
    const response = await apiClient.get<WeatherLogDto[]>(`/projects/${projectId}/weather-logs`, {
      params: { from: options?.from, to: options?.to },
    })
    return response.data
  } catch (error) {
    throw toWeatherApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/weather-logs` (S11-BE-01) — real, live endpoint. Creates an
 * `EntryKind = Original` entry. **There is no update/delete for this resource** — see
 * `WeatherRecordModal.tsx`'s mandatory pre-confirm warning, which is the actual DoD requirement
 * this endpoint's one-way nature exists to satisfy.
 *
 * `idempotencyKey` (S13-FE-01) is sent as the `Idempotency-Key` header, mirroring
 * `features/photo/api.ts#uploadPhoto`'s exact pattern — required (not optional) so a caller cannot
 * silently forget it the way an optional parameter invites; `weatherOutbox.ts`'s uploader is the only
 * caller and always has one (minted at enqueue, `services/outbox/outboxStore.ts#enqueue`). */
export async function recordWeatherLog(
  projectId: string,
  payload: RecordWeatherLogPayload,
  idempotencyKey: string,
): Promise<WeatherLogDto> {
  try {
    const response = await apiClient.post<WeatherLogDto>(`/projects/${projectId}/weather-logs`, payload, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })
    return response.data
  } catch (error) {
    throw toWeatherApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/weather-logs/{logId}/corrections` (S11-BE-01) — real, live
 * endpoint. `logId` must be the current chain tail (domain-rules.md §8.2 rule 2/3) — a stale target
 * fails with `WeatherLogAlreadySuperseded` (409), which the caller should treat as "reload and pick
 * the new tail", not retry blindly.
 *
 * `idempotencyKey` (S13-FE-01) — see `recordWeatherLog`'s identical remarks. */
export async function recordWeatherLogCorrection(
  projectId: string,
  logId: string,
  payload: RecordWeatherLogCorrectionPayload,
  idempotencyKey: string,
): Promise<WeatherLogDto> {
  try {
    const response = await apiClient.post<WeatherLogDto>(
      `/projects/${projectId}/weather-logs/${logId}/corrections`,
      payload,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
    return response.data
  } catch (error) {
    throw toWeatherApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/eot-evaluations` (S11-BE-02) — real, live endpoint. Runs one
 * `EotEvaluator` pass and persists it as a new, immutable `EotEvaluation` — **there is no `GET`**
 * (domain-rules.md §8.5 rule 3: re-evaluating is always a new record, never an edit), so this is the
 * only way to obtain a result; nothing survives a page reload except by running it again. Omitting
 * both window bounds (the default) uses the project's own `ContractStart`/data date (§1). */
export async function evaluateEot(
  projectId: string,
  payload: EvaluateEotPayload = { windowStart: null, windowEnd: null },
): Promise<EotEvaluationDto> {
  try {
    const response = await apiClient.post<EotEvaluationDto>(`/projects/${projectId}/eot-evaluations`, payload)
    return response.data
  } catch (error) {
    throw toWeatherApiError(error)
  }
}
