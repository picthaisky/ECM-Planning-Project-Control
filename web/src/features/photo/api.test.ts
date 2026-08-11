import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PhotoApiError, uploadPhoto } from './api'
import { apiClient } from '../../services/apiClient'
import type { PhotoDto } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { post: vi.fn() },
}))

function makeConfig(url: string): InternalAxiosRequestConfig {
  return { url, headers: new AxiosHeaders() } as InternalAxiosRequestConfig
}

function makeError(status: number, data: unknown): AxiosError {
  const config = makeConfig('/x')
  return new AxiosError('Request failed', String(status), config, undefined, {
    status,
    statusText: '',
    data,
    headers: {},
    config,
  })
}

const samplePhoto: PhotoDto = {
  id: 'photo-1',
  projectId: 'project-1',
  activityId: 'activity-1',
  caption: 'เทคอนกรีตพื้นชั้น 9',
  contentType: 'image/jpeg',
  fileSizeBytes: 204_800,
  uploadedByUserId: 'user-1',
  uploadedAt: '2026-07-08T09:00:00+07:00',
  capturedAt: '2026-07-08T08:55:00+07:00',
}

describe('features/photo/api#uploadPhoto', () => {
  beforeEach(() => {
    vi.mocked(apiClient.post).mockReset()
  })

  it('posts multipart form data with file/activityId/caption/capturedAt fields and the Idempotency-Key header', async () => {
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: samplePhoto })
    const blob = new Blob(['fake-jpeg-bytes'], { type: 'image/jpeg' })

    const result = await uploadPhoto(
      'project-1',
      blob,
      'site.jpg',
      { activityId: 'activity-1', caption: 'เทคอนกรีตพื้นชั้น 9', capturedAt: '2026-07-08T08:55:00.000Z' },
      'idem-key-1',
    )

    expect(result).toEqual(samplePhoto)
    expect(apiClient.post).toHaveBeenCalledTimes(1)
    const [url, body, config] = vi.mocked(apiClient.post).mock.calls[0]
    expect(url).toBe('/projects/project-1/photos')
    expect(body).toBeInstanceOf(FormData)
    const formData = body as FormData
    expect(formData.get('activityId')).toBe('activity-1')
    expect(formData.get('caption')).toBe('เทคอนกรีตพื้นชั้น 9')
    expect(formData.get('capturedAt')).toBe('2026-07-08T08:55:00.000Z')
    expect(formData.get('file')).toBeInstanceOf(Blob)
    expect(config?.headers).toEqual({ 'Idempotency-Key': 'idem-key-1' })
  })

  it('omits optional fields entirely from the form data when null', async () => {
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: samplePhoto })
    const blob = new Blob(['fake-jpeg-bytes'], { type: 'image/jpeg' })

    await uploadPhoto('project-1', blob, 'site.jpg', { activityId: null, caption: null, capturedAt: null }, 'idem-key-2')

    const [, body] = vi.mocked(apiClient.post).mock.calls[0]
    const formData = body as FormData
    expect(formData.get('activityId')).toBeNull()
    expect(formData.get('caption')).toBeNull()
    expect(formData.get('capturedAt')).toBeNull()
  })

  it('maps PhotoUnsupportedFormat to its Thai message', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce(
      makeError(400, { type: '/errors/photo-unsupported-format', detail: 'PhotoUnsupportedFormat' }),
    )

    await expect(uploadPhoto('project-1', new Blob(), 'x.jpg', { activityId: null, caption: null, capturedAt: null }, 'k')).rejects.toMatchObject(
      { message: 'รองรับเฉพาะไฟล์ภาพ JPEG หรือ PNG เท่านั้น' },
    )
  })

  it('maps PhotoFileTooLarge to its Thai message', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce(
      makeError(400, { type: '/errors/photo-file-too-large', detail: 'PhotoFileTooLarge' }),
    )

    await expect(uploadPhoto('project-1', new Blob(), 'x.jpg', { activityId: null, caption: null, capturedAt: null }, 'k')).rejects.toBeInstanceOf(
      PhotoApiError,
    )
  })

  it('falls back to the generic Thai message for an unmapped error code', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(500, { type: '/errors/unmapped', detail: 'SomethingElse' }))

    await expect(uploadPhoto('project-1', new Blob(), 'x.jpg', { activityId: null, caption: null, capturedAt: null }, 'k')).rejects.toMatchObject(
      { message: 'อัปโหลดรูปภาพไม่สำเร็จ กรุณาลองใหม่อีกครั้ง' },
    )
  })

  it('falls back to the generic Thai message for a non-Axios error', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce(new Error('boom'))

    await expect(uploadPhoto('project-1', new Blob(), 'x.jpg', { activityId: null, caption: null, capturedAt: null }, 'k')).rejects.toMatchObject(
      { message: 'อัปโหลดรูปภาพไม่สำเร็จ กรุณาลองใหม่อีกครั้ง' },
    )
  })
})
