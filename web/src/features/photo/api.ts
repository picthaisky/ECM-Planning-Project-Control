import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { ProblemDetails } from '../auth/types'
import type { PhotoDto, UploadPhotoFields } from './types'

/** Mirrors `features/weather/api.ts#WeatherApiError`'s established shape/discipline exactly. */
export class PhotoApiError extends Error {
  readonly status?: number
  /** Raw backend `ProblemDetails.detail`/type-slug code (e.g. `'IdempotencyPayloadMismatch'`) behind
   * the already-Thai `.message` — S13-FE-01: `photoOutbox.ts`'s uploader checks this against
   * `IDEMPOTENCY_CONFLICT_CODES` to decide `OutboxConflictError` vs an ordinary retryable throw,
   * without string-matching the translated message. `undefined` for a non-Axios error. */
  readonly code?: string

  constructor(message: string, status?: number, code?: string) {
    super(message)
    this.name = 'PhotoApiError'
    this.status = status
    this.code = code
  }
}

/** `PhotoErrorCodes` (backend) — transcribed from `ResultProblemMapper.cs`'s `KnownErrors` table for
 * the S12-BE-01 photo entries, not guessed. Double-keyed by the stable `detail` code and the `type`
 * URL's trailing slug, mirroring every other `features/*\/api.ts` error table in this codebase. */
const PHOTO_ERROR_TITLES: Record<string, string> = {
  PhotoProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  'not-found': 'ไม่พบข้อมูลที่ระบุ',

  PhotoActorRequired: 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',
  'photo-actor-required': 'ไม่สามารถระบุตัวตนผู้ใช้งานปัจจุบันได้ กรุณาเข้าสู่ระบบใหม่',

  PhotoUnknownActivity: 'รหัสกิจกรรมที่ระบุไม่อยู่ในโครงการนี้ กรุณาตรวจสอบอีกครั้ง',
  'photo-unknown-activity': 'รหัสกิจกรรมที่ระบุไม่อยู่ในโครงการนี้ กรุณาตรวจสอบอีกครั้ง',

  PhotoFileRequired: 'กรุณาแนบรูปภาพ',
  'photo-file-required': 'กรุณาแนบรูปภาพ',

  PhotoFileTooLarge: 'ไฟล์รูปภาพมีขนาดใหญ่เกินกำหนด',
  'photo-file-too-large': 'ไฟล์รูปภาพมีขนาดใหญ่เกินกำหนด',

  // S12-BE-01 DoD: magic-byte validation, not extension/Content-Type — a file this app itself
  // compressed to JPEG should never hit this, but a corrupt/foreign blob might.
  PhotoUnsupportedFormat: 'รองรับเฉพาะไฟล์ภาพ JPEG หรือ PNG เท่านั้น',
  'photo-unsupported-format': 'รองรับเฉพาะไฟล์ภาพ JPEG หรือ PNG เท่านั้น',

  PhotoMalformedImage: 'ไม่สามารถประมวลผลไฟล์ภาพนี้ได้ (โครงสร้างไฟล์ผิดปกติ)',
  'photo-malformed-image': 'ไม่สามารถประมวลผลไฟล์ภาพนี้ได้ (โครงสร้างไฟล์ผิดปกติ)',

  'validation-error': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
  'bad-request': 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',

  // S13-BE-01 `IdempotencyMiddleware` now wraps this endpoint too (`[Idempotent]` on
  // `ProjectPhotosController.Upload`) — see `services/outbox/errors.ts`'s remarks on which of these
  // are treated as a terminal outbox conflict versus an ordinary retryable failure.
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

const PHOTO_GENERIC_ERROR_MESSAGE = 'อัปโหลดรูปภาพไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

function toPhotoApiError(error: unknown): PhotoApiError {
  if (error instanceof AxiosError) {
    const status = error.response?.status
    const problem = error.response?.data as ProblemDetails | undefined
    const typeSlug = problem?.type?.split('/').pop()
    const code = (problem?.detail && PHOTO_ERROR_TITLES[problem.detail] ? problem.detail : typeSlug) ?? ''
    return new PhotoApiError(PHOTO_ERROR_TITLES[code] ?? PHOTO_GENERIC_ERROR_MESSAGE, status, code || undefined)
  }
  return new PhotoApiError(PHOTO_GENERIC_ERROR_MESSAGE)
}

/**
 * `POST /api/v1/projects/{projectId}/photos` (S12-BE-01) — real, live, multipart endpoint. Field
 * names (`file`/`activityId`/`caption`/`capturedAt`) match `ProjectPhotosController.Upload`'s
 * `[FromForm]` parameters exactly.
 *
 * `idempotencyKey` is sent as the `Idempotency-Key` header now (S12-FE-01) even though the backend
 * middleware that will honour it does not exist until S13-BE-01 (`docs/10.` S13-BE-01) — an unknown
 * header is simply ignored server-side today, and the outbox item already carries the key regardless
 * (`services/outbox/types.ts`'s own remarks on why the key cannot wait).
 */
export async function uploadPhoto(
  projectId: string,
  blob: Blob,
  fileName: string,
  fields: UploadPhotoFields,
  idempotencyKey: string,
): Promise<PhotoDto> {
  const formData = new FormData()
  formData.append('file', blob, fileName)
  if (fields.activityId) formData.append('activityId', fields.activityId)
  if (fields.caption) formData.append('caption', fields.caption)
  if (fields.capturedAt) formData.append('capturedAt', fields.capturedAt)

  try {
    const response = await apiClient.post<PhotoDto>(`/projects/${projectId}/photos`, formData, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })
    return response.data
  } catch (error) {
    throw toPhotoApiError(error)
  }
}
