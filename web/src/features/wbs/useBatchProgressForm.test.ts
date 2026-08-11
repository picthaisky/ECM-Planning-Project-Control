import 'fake-indexeddb/auto'
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useBatchProgressForm } from './useBatchProgressForm'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { AuthSession } from '../../store/authStore'
import type { ActivityForProgress } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, batchRecordProgress: vi.fn() }
})

const OWNER_SESSION: AuthSession = {
  accessToken: 'jwt',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-1',
  tenantId: 'tenant-1',
  role: 'PM',
}

/** S13-FE-01: submission now enqueues into the real (fake-indexeddb-polyfilled) `cmplus-outbox`
 * database before syncing — mirrors `features/photo/usePhotoOutbox.test.ts`'s identical reset
 * helper, needed to isolate this hook's outbox state between tests under the fixed production
 * database name. */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

const activities: ActivityForProgress[] = [
  { id: '11111111-1111-1111-1111-111111111111', activityCode: 'ACT-100', name: 'เทคอนกรีต', currentProgressPercentage: '40.00' },
  { id: '22222222-2222-2222-2222-222222222222', activityCode: 'ACT-200', name: 'ก่ออิฐ', currentProgressPercentage: '60.00' },
]

describe('useBatchProgressForm', () => {
  let onLineSpy: ReturnType<typeof vi.spyOn>

  beforeEach(async () => {
    await resetOutboxDatabase()
    vi.mocked(api.batchRecordProgress).mockReset()
    // Offline by default (mirrors `features/photo/usePhotoOutbox.test.ts`'s identical setup) so the
    // outbox's own mount-time/enqueue-time auto-sync trigger never races a test's own explicit
    // assertions about exactly when `batchRecordProgress` is called; tests that need the real API
    // call invoke `syncNow()` explicitly.
    onLineSpy = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    useAuthStore.getState().login(OWNER_SESSION)
  })

  afterEach(async () => {
    onLineSpy.mockRestore()
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('addActivities seeds rows pre-filled with the current %, and never duplicates an already-added activity', () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))

    act(() => result.current.addActivities(activities))
    expect(result.current.rows).toHaveLength(2)
    expect(result.current.rows[0].newProgressPercentage).toBe('40.00')

    act(() => result.current.addActivities([activities[0]]))
    expect(result.current.rows).toHaveLength(2) // no duplicate
  })

  it('addManualRow accepts a well-formed GUID and rejects anything else in Thai', () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))

    let error: string | null = null
    act(() => {
      error = result.current.addManualRow('not-a-guid')
    })
    expect(error).toContain('GUID')
    expect(result.current.rows).toHaveLength(0)

    act(() => {
      error = result.current.addManualRow('33333333-3333-3333-3333-333333333333')
    })
    expect(error).toBeNull()
    expect(result.current.rows).toHaveLength(1)
    expect(result.current.rows[0].currentProgressPercentage).toBeNull() // unknown for a manual row
  })

  it('attemptSubmit rejects client-side when a row is out of 0-100 range, without ever enqueueing', async () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '150'))

    await act(async () => {
      await result.current.attemptSubmit()
    })

    expect(result.current.validationError).toContain('0.00 ถึง 100.00')
    expect(result.current.rows).toHaveLength(2) // still there — never queued
    expect(result.current.outboxItems).toEqual([])
  })

  it('a lower new % than current opens the decrease-confirmation gate instead of submitting immediately', async () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '20')) // 40 -> 20 is a decrease

    await act(async () => {
      await result.current.attemptSubmit()
    })

    expect(result.current.pendingConfirmation).toBe(true)
    expect(result.current.decreasedRows).toHaveLength(1)
    expect(result.current.outboxItems).toEqual([]) // not queued yet — still gated
  })

  it('confirming the decrease enqueues the batch (ADR-0005: always outbox) and clears the grid', async () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '20'))

    await act(async () => {
      await result.current.attemptSubmit()
    })
    expect(result.current.pendingConfirmation).toBe(true)

    await act(async () => {
      await result.current.confirmDecreaseAndSubmit()
    })

    expect(result.current.rows).toHaveLength(0)
    expect(result.current.lastResultCount).toBe(2)
    expect(result.current.pendingConfirmation).toBe(false)
    await waitFor(() => expect(result.current.outboxItems).toHaveLength(1))
    expect(result.current.outboxItems[0].status).toBe('queued') // offline — not yet attempted
    expect(result.current.outboxItems[0].payload.request).toEqual({
      periodEndDate: '2026-07-27T00:00:00.000Z',
      entries: [
        { activityId: activities[0].id, progressPercentage: '20', actualQuantity: null },
        { activityId: activities[1].id, progressPercentage: '60.00', actualQuantity: null },
      ],
    })
    expect(api.batchRecordProgress).not.toHaveBeenCalled()
  })

  it('submits directly (no confirmation) when nothing decreased, and multiple activities go in one batch', async () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '55')) // 40 -> 55, an increase

    await act(async () => {
      await result.current.attemptSubmit()
    })

    expect(result.current.pendingConfirmation).toBe(false)
    await waitFor(() => expect(result.current.outboxItems).toHaveLength(1))
    expect(result.current.outboxItems[0].payload.request.entries).toHaveLength(2)
  })

  it('syncNow() uploads the queued batch and marks it synced with entriesRecorded as the stored count', async () => {
    vi.mocked(api.batchRecordProgress).mockResolvedValueOnce({ entriesRecorded: 2 })
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))

    await act(async () => {
      await result.current.attemptSubmit()
    })
    await waitFor(() => expect(result.current.outboxItems).toHaveLength(1))

    await act(async () => {
      await result.current.syncNow()
    })

    expect(api.batchRecordProgress).toHaveBeenCalledWith(
      'project-1',
      {
        periodEndDate: '2026-07-27T00:00:00.000Z',
        entries: [
          { activityId: activities[0].id, progressPercentage: '40.00', actualQuantity: null },
          { activityId: activities[1].id, progressPercentage: '60.00', actualQuantity: null },
        ],
      },
      expect.any(String),
    )
    await waitFor(() => expect(result.current.outboxItems[0].status).toBe('synced'))
    expect(result.current.outboxItems[0].serverId).toBe('2')
  })

  it('a batch that fails to sync stays visible with its real Thai error, and the edited rows are not resurrected', async () => {
    vi.mocked(api.batchRecordProgress).mockRejectedValueOnce(
      new api.WbsApiError('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง', 400, 'ProgressUnknownActivity'),
    )
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))

    await act(async () => {
      await result.current.attemptSubmit()
    })
    await waitFor(() => expect(result.current.outboxItems).toHaveLength(1))
    expect(result.current.rows).toHaveLength(0) // already cleared at enqueue time — ADR-0005

    await act(async () => {
      await result.current.syncNow()
    })

    await waitFor(() => expect(result.current.outboxItems[0].status).toBe('failed'))
    expect(result.current.outboxItems[0].lastError).toContain('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้')
  })
})
