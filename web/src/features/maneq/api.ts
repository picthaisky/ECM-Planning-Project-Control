import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type {
  ActivityOptionDto,
  ManpowerLogDto,
  ProductivityIndexResponseDto,
  RecordManpowerLogCorrectionPayload,
  RecordManpowerLogPayload,
  WbsNodeOptionDto,
  WeatherLogOptionDto,
  WorkCategoryDto,
} from './types'

/** Minimal view of the weather-log list response this feature parses (it carries more fields). */
interface RawWeatherLog {
  id: string
  logDate: string
  condition: string
}

/** Minimal view of the WBS-tree response this feature parses into a flat node list - deliberately
 * NOT importing the wbs feature's own types, to keep the modules decoupled. */
interface RawWbsTreeNode {
  id: string
  code: string
  title: string
  children: RawWbsTreeNode[]
}
interface RawWbsTree {
  rootNodes: RawWbsTreeNode[]
}

/** Mirrors `features/weather/api.ts#WeatherApiError`'s established shape/discipline exactly. */
export class ManpowerApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ManpowerApiError'
    this.status = status
  }
}

/** `ManpowerLogErrorCodes` (backend) — transcribed from `ResultProblemMapper.cs`'s `KnownErrors`
 * table for the S12-BE-02 entries, not guessed. Double-keyed by the stable `detail` code and the
 * `type` URL's trailing slug, mirroring every other `features/*\/api.ts` error table. */
const MANPOWER_ERROR_TITLES: Record<string, string> = {
  ManpowerLogProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',

  ManpowerLogActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'manpower-log-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  ManpowerLogWorkCategoryNotInProject: 'รหัสหมวดงาน (Work Category) ที่ระบุไม่อยู่ในโครงการนี้',
  'manpower-log-work-category-not-in-project': 'รหัสหมวดงาน (Work Category) ที่ระบุไม่อยู่ในโครงการนี้',

  ManpowerLogWbsNodeNotInProject: 'รหัส WBS Node ที่ระบุอยู่คนละโครงการ',
  'manpower-log-wbs-node-not-in-project': 'รหัส WBS Node ที่ระบุอยู่คนละโครงการ',

  ManpowerLogWbsNodeNotFound: 'ไม่พบรหัส WBS Node ที่ระบุ',
  'manpower-log-wbs-node-not-found': 'ไม่พบรหัส WBS Node ที่ระบุ',

  ManpowerLogActivityNotInProject: 'รหัสกิจกรรมที่ระบุอยู่คนละโครงการ',
  'manpower-log-activity-not-in-project': 'รหัสกิจกรรมที่ระบุอยู่คนละโครงการ',

  ManpowerLogActivityNotFound: 'ไม่พบรหัสกิจกรรมที่ระบุ',
  'manpower-log-activity-not-found': 'ไม่พบรหัสกิจกรรมที่ระบุ',

  ManpowerLogActivityWbsNodeMismatch: 'WBS Node ของกิจกรรมที่ระบุไม่ตรงกับ WBS Node ของรายการนี้',
  'manpower-log-activity-wbs-node-mismatch': 'WBS Node ของกิจกรรมที่ระบุไม่ตรงกับ WBS Node ของรายการนี้',

  ManpowerLogAlreadyExists:
    'มีบันทึกสำหรับวันที่/กะ/หมวดงาน/WBS Node/ประเภทแรงงานนี้อยู่แล้ว หากเป็นอีกทีมงานจริง ให้ยืนยันบันทึกซ้ำอีกครั้ง',
  'manpower-log-already-exists':
    'มีบันทึกสำหรับวันที่/กะ/หมวดงาน/WBS Node/ประเภทแรงงานนี้อยู่แล้ว หากเป็นอีกทีมงานจริง ให้ยืนยันบันทึกซ้ำอีกครั้ง',

  ManpowerLogCorrectionTargetNotFound: 'ไม่พบบันทึกต้นฉบับที่ต้องการแก้ไขในโครงการนี้',
  'manpower-log-correction-target-not-found': 'ไม่พบบันทึกต้นฉบับที่ต้องการแก้ไขในโครงการนี้',

  ManpowerLogAlreadySuperseded:
    'บันทึกนี้มีรายการแก้ไขอื่นอยู่แล้ว กรุณาโหลดข้อมูลใหม่แล้วแก้ไขจากรายการล่าสุดของสายการแก้ไขนี้แทน',
  'manpower-log-already-superseded':
    'บันทึกนี้มีรายการแก้ไขอื่นอยู่แล้ว กรุณาโหลดข้อมูลใหม่แล้วแก้ไขจากรายการล่าสุดของสายการแก้ไขนี้แทน',

  ManpowerLogCorrectionOrdering: 'ไม่สามารถบันทึกรายการแก้ไขได้ (ลำดับเวลาของรายการไม่ถูกต้อง)',
  'manpower-log-correction-ordering': 'ไม่สามารถบันทึกรายการแก้ไขได้ (ลำดับเวลาของรายการไม่ถูกต้อง)',

  ManpowerLogIsImmutable: 'ไม่สามารถแก้ไขหรือลบบันทึกกำลังคน/เครื่องจักรได้ กรุณาบันทึกรายการแก้ไขใหม่แทน',
  'manpower-log-is-immutable': 'ไม่สามารถแก้ไขหรือลบบันทึกกำลังคน/เครื่องจักรได้ กรุณาบันทึกรายการแก้ไขใหม่แทน',

  ManpowerLogInvalidDateRange: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',
  'manpower-log-invalid-date-range': 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด)',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
}

const MANPOWER_GENERIC_ERROR_MESSAGE = 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toManpowerApiError(error: unknown): ManpowerApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && MANPOWER_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    return new ManpowerApiError(MANPOWER_ERROR_TITLES[code] ?? MANPOWER_GENERIC_ERROR_MESSAGE, status)
  }
  return new ManpowerApiError(MANPOWER_GENERIC_ERROR_MESSAGE)
}

/** `POST /api/v1/projects/{projectId}/manpower-logs` (S12-BE-02) — real, live endpoint. Creates an
 * `EntryKind = Original` entry. */
export async function recordManpowerLog(projectId: string, payload: RecordManpowerLogPayload): Promise<ManpowerLogDto> {
  try {
    const response = await apiClient.post<ManpowerLogDto>(`/projects/${projectId}/manpower-logs`, payload)
    return response.data
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

/** `POST /api/v1/projects/{projectId}/manpower-logs/{logId}/corrections` (S12-BE-02) — real, live
 * endpoint. `logId` must be the current chain tail (domain-rules.md §4.7 rule 2/3). */
export async function recordManpowerLogCorrection(
  projectId: string,
  logId: string,
  payload: RecordManpowerLogCorrectionPayload,
): Promise<ManpowerLogDto> {
  try {
    const response = await apiClient.post<ManpowerLogDto>(
      `/projects/${projectId}/manpower-logs/${logId}/corrections`,
      payload,
    )
    return response.data
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

export interface GetProductivityIndexParams {
  wbsNodeId?: string | null
  activityId?: string | null
  /** ISO instant; omit for a cumulative read from project start (§5.2). */
  from?: string | null
  /** ISO instant; required — the half-open period's inclusive upper bound (§4.5). */
  to: string
}

/** `GET /api/v1/projects/{projectId}/manpower-logs/productivity-index` (S12-BE-02) — real, live
 * endpoint; the only read this Sprint 12 backend exposes for manpower data (there is no `GET` list
 * of raw log rows and no work-category catalogue endpoint — confirmed directly against
 * `ProjectManpowerLogsController.cs`, not assumed; see `ManeqPage.tsx`'s own remarks on the resulting
 * screen-scope consequences). */
/** `GET /api/v1/projects/{projectId}/wbs-tree` — the live WBS tree (S4-BE-01), flattened here into a
 * flat node list for the record/correction form's (optional) WBS-node dropdown. Depth-first order, so
 * parents precede their children. */
export async function listWbsNodes(projectId: string): Promise<WbsNodeOptionDto[]> {
  try {
    const response = await apiClient.get<RawWbsTree>(`/projects/${projectId}/wbs-tree`)
    const flat: WbsNodeOptionDto[] = []
    const walk = (nodes: RawWbsTreeNode[]) => {
      for (const node of nodes) {
        flat.push({ id: node.id, code: node.code, title: node.title })
        walk(node.children ?? [])
      }
    }
    walk(response.data.rootNodes ?? [])
    return flat
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

/** `GET /api/v1/projects/{projectId}/weather-logs` (S11-BE-01) — the project's weather logs, for the
 * form's (optional) related-weather-log dropdown. Newest first (the endpoint's own order). */
export async function listWeatherLogs(projectId: string): Promise<WeatherLogOptionDto[]> {
  try {
    const response = await apiClient.get<RawWeatherLog[]>(`/projects/${projectId}/weather-logs`)
    return response.data.map((log) => ({ id: log.id, logDate: log.logDate, condition: log.condition }))
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

/** `GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities` (S4-BE-05) — the activities
 * under one WBS node, for the form's dependent activity dropdown (populated from the selected node). */
export async function listNodeActivities(projectId: string, wbsNodeId: string): Promise<ActivityOptionDto[]> {
  try {
    const response = await apiClient.get<ActivityOptionDto[]>(
      `/projects/${projectId}/wbs-nodes/${wbsNodeId}/activities`,
    )
    return response.data
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

/** `GET /api/v1/projects/{projectId}/work-categories` (S12 catalogue gap closure) — real, live
 * endpoint. Returns the project's active work-category catalogue (tenant-wide defaults + any
 * project-specific entries), ordered by display order. Backs the record/correction form's dropdown. */
export async function listWorkCategories(projectId: string): Promise<WorkCategoryDto[]> {
  try {
    const response = await apiClient.get<WorkCategoryDto[]>(`/projects/${projectId}/work-categories`)
    return response.data
  } catch (error) {
    throw toManpowerApiError(error)
  }
}

export async function getProductivityIndex(
  projectId: string,
  params: GetProductivityIndexParams,
): Promise<ProductivityIndexResponseDto> {
  try {
    const response = await apiClient.get<ProductivityIndexResponseDto>(
      `/projects/${projectId}/manpower-logs/productivity-index`,
      { params: { wbsNodeId: params.wbsNodeId ?? undefined, activityId: params.activityId ?? undefined, from: params.from ?? undefined, to: params.to } },
    )
    return response.data
  } catch (error) {
    throw toManpowerApiError(error)
  }
}
