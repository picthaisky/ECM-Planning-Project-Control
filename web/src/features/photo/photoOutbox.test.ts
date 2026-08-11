import { beforeEach, describe, expect, it, vi } from 'vitest'
import { uploadPhotoOutboxItem } from './photoOutbox'
import { uploadPhoto, PhotoApiError } from './api'
import { OutboxConflictError } from '../../services/outbox'
import type { OutboxItem } from '../../services/outbox'
import type { PhotoOutboxPayload } from './photoOutbox'

vi.mock('./api', () => ({
  uploadPhoto: vi.fn(),
  PhotoApiError: class PhotoApiError extends Error {
    status?: number
    code?: string
    constructor(message: string, status?: number, code?: string) {
      super(message)
      this.name = 'PhotoApiError'
      this.status = status
      this.code = code
    }
  },
}))

function makeItem(overrides: Partial<OutboxItem<PhotoOutboxPayload>> = {}): OutboxItem<PhotoOutboxPayload> {
  return {
    id: 'item-1',
    kind: 'photo',
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: { projectId: 'project-1', fields: { activityId: null, caption: null, capturedAt: null }, fileName: 'a.jpg' },
    blob: new Blob(['bytes'], { type: 'image/jpeg' }),
    blobFileName: 'a.jpg',
    blobContentType: 'image/jpeg',
    status: 'syncing',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-07-08T09:00:00.000Z',
    updatedAt: '2026-07-08T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

describe('uploadPhotoOutboxItem', () => {
  beforeEach(() => {
    vi.mocked(uploadPhoto).mockReset()
  })

  it('forwards projectId/blob/fileName/fields/idempotencyKey to uploadPhoto and returns the server id', async () => {
    vi.mocked(uploadPhoto).mockResolvedValueOnce({
      id: 'server-photo-1',
      projectId: 'project-1',
      activityId: null,
      caption: null,
      contentType: 'image/jpeg',
      fileSizeBytes: 100,
      uploadedByUserId: 'user-1',
      uploadedAt: '2026-07-08T09:00:00.000Z',
      capturedAt: '2026-07-08T09:00:00.000Z',
    })

    const item = makeItem()
    const result = await uploadPhotoOutboxItem(item)

    expect(result).toEqual({ serverId: 'server-photo-1' })
    expect(uploadPhoto).toHaveBeenCalledWith('project-1', item.blob, 'a.jpg', item.payload.fields, 'idem-1')
  })

  it('throws a Thai error, without calling uploadPhoto, when the item has no blob', async () => {
    const item = makeItem({ blob: null })

    await expect(uploadPhotoOutboxItem(item)).rejects.toThrow(/ไม่พบไฟล์รูปภาพ/)
    expect(uploadPhoto).not.toHaveBeenCalled()
  })

  describe('S13-FE-01: idempotency 409 handling', () => {
    it('maps a 409 IdempotencyPayloadMismatch to OutboxConflictError, not an ordinary retryable throw', async () => {
      vi.mocked(uploadPhoto).mockRejectedValueOnce(
        new PhotoApiError('รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้', 409, 'IdempotencyPayloadMismatch'),
      )

      const item = makeItem()
      const error = await uploadPhotoOutboxItem(item).catch((e: unknown) => e)

      expect(error).toBeInstanceOf(OutboxConflictError)
      expect((error as Error).message).toBe('รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้')
    })

    it('leaves a 409 IdempotencyRequestInProgress as an ordinary (retryable) error', async () => {
      vi.mocked(uploadPhoto).mockRejectedValueOnce(
        new PhotoApiError('มีการส่งรายการนี้อยู่แล้ว', 409, 'IdempotencyRequestInProgress'),
      )

      const item = makeItem()
      const error = await uploadPhotoOutboxItem(item).catch((e: unknown) => e)

      expect(error).not.toBeInstanceOf(OutboxConflictError)
      expect(error).toBeInstanceOf(PhotoApiError)
    })

    it('leaves a non-409 failure as an ordinary (retryable) error', async () => {
      vi.mocked(uploadPhoto).mockRejectedValueOnce(new PhotoApiError('ไม่พบโครงการที่ระบุ', 404, 'PhotoProjectNotFound'))

      const item = makeItem()
      const error = await uploadPhotoOutboxItem(item).catch((e: unknown) => e)

      expect(error).not.toBeInstanceOf(OutboxConflictError)
    })
  })
})
