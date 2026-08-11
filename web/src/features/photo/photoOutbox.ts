import { uploadPhoto, PhotoApiError } from './api'
import { IDEMPOTENCY_CONFLICT_CODES, OutboxConflictError } from '../../services/outbox'
import type { OutboxUploader } from '../../services/outbox'
import type { UploadPhotoFields } from './types'

/** The one `kind` S12-FE-01 registers. S13-FE-01 adds `'weather-log'`/`'progress-batch'` alongside
 * it in the same generic queue — see `services/outbox/types.ts`'s class remarks. */
export const PHOTO_OUTBOX_KIND = 'photo'

export interface PhotoOutboxPayload {
  projectId: string
  fields: UploadPhotoFields
  fileName: string
}

/**
 * `syncEngine.ts`'s uploader for `kind: 'photo'` — turns one queued outbox item back into the real
 * `uploadPhoto` call. Reads the compressed bytes from `item.blob` (never re-compresses here; that
 * already happened once, client-side, before the item was ever enqueued —
 * `usePhotoOutbox.ts#capture`) and forwards the item's own `idempotencyKey`, minted once at enqueue
 * time (`services/outbox/outboxStore.ts#enqueue`), so a retry after a lost response reuses the exact
 * same key rather than minting a new one that could not protect the item added before an upgrade.
 */
export const uploadPhotoOutboxItem: OutboxUploader<PhotoOutboxPayload> = async (item) => {
  if (!item.blob) {
    // Should not happen in practice (a photo item always enqueues with a blob — see
    // `usePhotoOutbox.ts#capture`) but IndexedDB storage could in principle be cleared/corrupted by
    // the browser under storage pressure; fail loud with a Thai message rather than upload nothing.
    throw new Error('ไม่พบไฟล์รูปภาพของรายการนี้ในเครื่อง (อาจถูกล้างแคช) กรุณาถ่ายรูปนี้ใหม่')
  }

  try {
    const photo = await uploadPhoto(
      item.payload.projectId,
      item.blob,
      item.payload.fileName,
      item.payload.fields,
      item.idempotencyKey,
    )
    return { serverId: photo.id }
  } catch (error) {
    // S13-FE-01: S13-BE-01's `IdempotencyMiddleware` now wraps this endpoint too. A same-key-
    // different-payload (or non-replayable) 409 means blind retry will never succeed — route it to
    // the outbox's terminal `conflict` status instead of the ordinary retryable `failed` bucket (see
    // `services/outbox/errors.ts`). `IdempotencyRequestInProgress` (a transient in-flight collision,
    // e.g. the sync-status badge racing this page's own auto-sync) is deliberately *not* in that set,
    // so it falls through and stays a normal retryable failure.
    if (error instanceof PhotoApiError && error.status === 409 && error.code && IDEMPOTENCY_CONFLICT_CODES.has(error.code)) {
      throw new OutboxConflictError(error.message)
    }
    throw error
  }
}
