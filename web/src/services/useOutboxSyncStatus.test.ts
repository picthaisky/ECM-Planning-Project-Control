import 'fake-indexeddb/auto'
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useOutboxSyncStatus } from './useOutboxSyncStatus'
import { createIndexedDbOutboxStorage, createOutboxStore } from './outbox'
import { useAuthStore } from '../store/authStore'
import type { AuthSession } from '../store/authStore'
import * as photoApi from '../features/photo/api'
import * as weatherApi from '../features/weather/api'
import * as wbsApi from '../features/wbs/api'

vi.mock('../features/photo/api', async () => {
  const actual = await vi.importActual<typeof import('../features/photo/api')>('../features/photo/api')
  return { ...actual, uploadPhoto: vi.fn() }
})
vi.mock('../features/weather/api', async () => {
  const actual = await vi.importActual<typeof import('../features/weather/api')>('../features/weather/api')
  return { ...actual, recordWeatherLog: vi.fn(), recordWeatherLogCorrection: vi.fn() }
})
vi.mock('../features/wbs/api', async () => {
  const actual = await vi.importActual<typeof import('../features/wbs/api')>('../features/wbs/api')
  return { ...actual, batchRecordProgress: vi.fn() }
})

const OWNER_SESSION: AuthSession = {
  accessToken: 'jwt',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-a',
  tenantId: 'tenant-1',
  role: 'PM',
}

function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

/** Writes directly through a second `createOutboxStore` instance against the same real IndexedDB
 * database — the hook under test always creates its own; this is how these tests seed state without
 * depending on the hook's own internals. */
function directStore() {
  return createOutboxStore({
    storage: createIndexedDbOutboxStorage(),
    getOwner: () => ({ userId: OWNER_SESSION.userId, tenantId: OWNER_SESSION.tenantId }),
  })
}

describe('useOutboxSyncStatus', () => {
  beforeEach(async () => {
    await resetOutboxDatabase()
    vi.mocked(photoApi.uploadPhoto).mockReset()
    vi.mocked(weatherApi.recordWeatherLog).mockReset()
    vi.mocked(weatherApi.recordWeatherLogCorrection).mockReset()
    vi.mocked(wbsApi.batchRecordProgress).mockReset()
    useAuthStore.getState().login(OWNER_SESSION)
  })

  afterEach(async () => {
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('aggregates counts across every kind for the current owner', async () => {
    const store = directStore()
    const queued = await store.enqueue({ kind: 'photo', payload: {}, blob: new Blob(['a'], { type: 'image/jpeg' }) })
    const failed = await store.enqueue({ kind: 'weather-log', payload: {} })
    await store.markFailed(failed.id, 'เครือข่ายขัดข้อง')
    const conflicted = await store.enqueue({ kind: 'progress-batch', payload: {} })
    await store.markConflict(conflicted.id, 'ข้อมูลขัดแย้ง')
    const synced = await store.enqueue({ kind: 'photo', payload: {} })
    await store.markSynced(synced.id, 'server-1')
    void queued

    const { result } = renderHook(() => useOutboxSyncStatus())

    await waitFor(() => expect(result.current.items).toHaveLength(4))
    expect(result.current.pendingCount).toBe(2) // queued + failed (not conflict, not synced)
    expect(result.current.failedCount).toBe(1)
    expect(result.current.conflictCount).toBe(1)
    expect(result.current.pendingBlobCount).toBe(1) // only the queued photo still has a blob
  })

  it('scopes strictly to the current owner — another owner\'s items are invisible (H-02 seam reused)', async () => {
    const storage = createIndexedDbOutboxStorage()
    const otherOwnerStore = createOutboxStore({ storage, getOwner: () => ({ userId: 'someone-else', tenantId: 'tenant-1' }) })
    await otherOwnerStore.enqueue({ kind: 'photo', payload: {} })

    const { result } = renderHook(() => useOutboxSyncStatus())

    await waitFor(() => expect(result.current.pendingCount).toBe(0))
    expect(result.current.items).toEqual([])
  })

  it('reconciles a stale `syncing` item left by an interrupted previous session, across every kind, without a feature page ever mounting', async () => {
    const store = directStore()
    const stranded = await store.enqueue({ kind: 'progress-batch', payload: {} })
    await store.markSyncing(stranded.id)

    const { result } = renderHook(() => useOutboxSyncStatus())

    await waitFor(() => expect(result.current.pendingCount).toBe(1))
    expect(result.current.items[0].status).toBe('failed') // reconciled, hence retryable again
  })

  it('syncNow() flushes every kind via the shared registry, from a page that mounted none of the feature hooks', async () => {
    vi.mocked(photoApi.uploadPhoto).mockResolvedValueOnce({
      id: 'server-photo-1',
      projectId: 'project-1',
      activityId: null,
      caption: null,
      contentType: 'image/jpeg',
      fileSizeBytes: 10,
      uploadedByUserId: 'user-a',
      uploadedAt: '2026-08-11T09:00:00.000Z',
      capturedAt: '2026-08-11T09:00:00.000Z',
    })
    const store = directStore()
    await store.enqueue({
      kind: 'photo',
      payload: { projectId: 'project-1', fields: { activityId: null, caption: null, capturedAt: null }, fileName: 'a.jpg' },
      blob: new Blob(['x'], { type: 'image/jpeg' }),
      blobFileName: 'a.jpg',
      blobContentType: 'image/jpeg',
    })

    const { result } = renderHook(() => useOutboxSyncStatus())
    await waitFor(() => expect(result.current.pendingCount).toBe(1))

    await act(async () => {
      await result.current.syncNow()
    })

    expect(photoApi.uploadPhoto).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(result.current.pendingCount).toBe(0))
    expect(result.current.items[0].status).toBe('synced')
  })

  it('a conflicted item is never retried by syncNow() (terminal, per OutboxConflictError)', async () => {
    vi.mocked(wbsApi.batchRecordProgress).mockRejectedValue(
      new wbsApi.WbsApiError('ชุดข้อมูลนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้', 409, 'IdempotencyPayloadMismatch'),
    )
    const store = directStore()
    await store.enqueue({
      kind: 'progress-batch',
      payload: { projectId: 'project-1', request: { periodEndDate: '2026-08-11T00:00:00.000Z', entries: [] } },
    })

    const { result } = renderHook(() => useOutboxSyncStatus())
    await waitFor(() => expect(result.current.pendingCount).toBe(1))

    await act(async () => {
      await result.current.syncNow()
    })
    await waitFor(() => expect(result.current.conflictCount).toBe(1))
    expect(wbsApi.batchRecordProgress).toHaveBeenCalledTimes(1)

    await act(async () => {
      await result.current.syncNow()
    })
    expect(wbsApi.batchRecordProgress).toHaveBeenCalledTimes(1) // not retried
  })
})
