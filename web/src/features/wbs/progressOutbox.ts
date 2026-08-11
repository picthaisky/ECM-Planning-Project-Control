import { batchRecordProgress, WbsApiError } from './api'
import { IDEMPOTENCY_CONFLICT_CODES, OutboxConflictError } from '../../services/outbox'
import type { OutboxUploader } from '../../services/outbox'
import type { BatchProgressRequest } from './types'

/**
 * S13-FE-01: extends the generic outbox (`services/outbox/`, ADR-0005) to the WBS batch
 * "โหมดอัปเดตความคืบหน้า" grid (S4-FE-03) — the third `kind`, mirroring `photoOutbox.ts`'s shape
 * exactly (a bare top-level uploader function; no cross-item dependency the way a weather-log
 * correction has).
 */
export const PROGRESS_BATCH_OUTBOX_KIND = 'progress-batch'

export interface ProgressBatchOutboxPayload {
  projectId: string
  request: BatchProgressRequest
}

/**
 * `syncEngine.ts`'s uploader for `kind: PROGRESS_BATCH_OUTBOX_KIND`. A batch has no single created
 * resource id the way a photo/weather-log row does (`POST .../progress/batch` writes N
 * `ActivityProgressLog` rows and returns only their count) — `serverId` is repurposed to hold that
 * confirmed `entriesRecorded` count, as a decimal string, purely so the UI
 * (`ProgressOutboxQueue.tsx`) can render "ซิงค์สำเร็จ N รายการ" once synced, without growing the
 * shared, kind-agnostic `OutboxItem` shape for one kind's own display need.
 */
export const uploadProgressBatchOutboxItem: OutboxUploader<ProgressBatchOutboxPayload> = async (item) => {
  try {
    const result = await batchRecordProgress(item.payload.projectId, item.payload.request, item.idempotencyKey)
    return { serverId: String(result.entriesRecorded) }
  } catch (error) {
    // S13-FE-01: S13-BE-01's `IdempotencyMiddleware` now wraps this endpoint too. See
    // `services/outbox/errors.ts` for which codes are a terminal outbox conflict versus an ordinary
    // retryable failure.
    if (error instanceof WbsApiError && error.status === 409 && error.code && IDEMPOTENCY_CONFLICT_CODES.has(error.code)) {
      throw new OutboxConflictError(error.message)
    }
    throw error
  }
}
