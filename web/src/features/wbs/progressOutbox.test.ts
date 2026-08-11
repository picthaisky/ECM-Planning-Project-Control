import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PROGRESS_BATCH_OUTBOX_KIND, uploadProgressBatchOutboxItem } from './progressOutbox'
import { batchRecordProgress, WbsApiError } from './api'
import { OutboxConflictError } from '../../services/outbox'
import type { OutboxItem } from '../../services/outbox'
import type { ProgressBatchOutboxPayload } from './progressOutbox'

vi.mock('./api', () => ({
  batchRecordProgress: vi.fn(),
  WbsApiError: class WbsApiError extends Error {
    status?: number
    code?: string
    constructor(message: string, status?: number, code?: string) {
      super(message)
      this.name = 'WbsApiError'
      this.status = status
      this.code = code
    }
  },
}))

function makeItem(overrides: Partial<OutboxItem<ProgressBatchOutboxPayload>> = {}): OutboxItem<ProgressBatchOutboxPayload> {
  return {
    id: 'item-1',
    kind: PROGRESS_BATCH_OUTBOX_KIND,
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: {
      projectId: 'project-1',
      request: {
        periodEndDate: '2026-08-11T00:00:00.000Z',
        entries: [{ activityId: 'activity-1', progressPercentage: '55.00', actualQuantity: null }],
      },
    },
    blob: null,
    blobFileName: null,
    blobContentType: null,
    status: 'syncing',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-08-11T09:00:00.000Z',
    updatedAt: '2026-08-11T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

describe('uploadProgressBatchOutboxItem', () => {
  beforeEach(() => {
    vi.mocked(batchRecordProgress).mockReset()
  })

  it('forwards projectId/request to batchRecordProgress and stores entriesRecorded as the serverId', async () => {
    vi.mocked(batchRecordProgress).mockResolvedValueOnce({ entriesRecorded: 3 })

    const item = makeItem()
    const result = await uploadProgressBatchOutboxItem(item)

    expect(result).toEqual({ serverId: '3' })
    expect(batchRecordProgress).toHaveBeenCalledWith('project-1', item.payload.request, item.idempotencyKey)
  })

  it('maps a 409 IdempotencyPayloadMismatch to OutboxConflictError', async () => {
    vi.mocked(batchRecordProgress).mockRejectedValueOnce(
      new WbsApiError('ชุดข้อมูลนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้', 409, 'IdempotencyPayloadMismatch'),
    )

    const error = await uploadProgressBatchOutboxItem(makeItem()).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(OutboxConflictError)
  })

  it('leaves a 409 IdempotencyRequestInProgress as an ordinary retryable error', async () => {
    vi.mocked(batchRecordProgress).mockRejectedValueOnce(
      new WbsApiError('มีการส่งชุดข้อมูลนี้อยู่แล้ว', 409, 'IdempotencyRequestInProgress'),
    )

    const error = await uploadProgressBatchOutboxItem(makeItem()).catch((e: unknown) => e)
    expect(error).not.toBeInstanceOf(OutboxConflictError)
  })

  it('leaves a validation failure (e.g. unknown activity) as an ordinary retryable error', async () => {
    vi.mocked(batchRecordProgress).mockRejectedValueOnce(
      new WbsApiError('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้', 400, 'ProgressUnknownActivity'),
    )

    const error = await uploadProgressBatchOutboxItem(makeItem()).catch((e: unknown) => e)
    expect(error).not.toBeInstanceOf(OutboxConflictError)
    expect(error).toBeInstanceOf(WbsApiError)
  })
})
