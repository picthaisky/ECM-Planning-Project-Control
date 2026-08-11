import { uploadPhoto } from './api'
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

  const photo = await uploadPhoto(
    item.payload.projectId,
    item.blob,
    item.payload.fileName,
    item.payload.fields,
    item.idempotencyKey,
  )
  return { serverId: photo.id }
}
