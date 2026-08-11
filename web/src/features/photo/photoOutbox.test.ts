import { beforeEach, describe, expect, it, vi } from 'vitest'
import { uploadPhotoOutboxItem } from './photoOutbox'
import { uploadPhoto } from './api'
import type { OutboxItem } from '../../services/outbox'
import type { PhotoOutboxPayload } from './photoOutbox'

vi.mock('./api', () => ({ uploadPhoto: vi.fn() }))

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
})
