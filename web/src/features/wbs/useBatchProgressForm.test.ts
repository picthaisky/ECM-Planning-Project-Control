import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useBatchProgressForm } from './useBatchProgressForm'
import * as api from './api'
import type { ActivityForProgress } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, batchRecordProgress: vi.fn() }
})

const activities: ActivityForProgress[] = [
  { id: '11111111-1111-1111-1111-111111111111', activityCode: 'ACT-100', name: 'เทคอนกรีต', currentProgressPercentage: '40.00' },
  { id: '22222222-2222-2222-2222-222222222222', activityCode: 'ACT-200', name: 'ก่ออิฐ', currentProgressPercentage: '60.00' },
]

describe('useBatchProgressForm', () => {
  beforeEach(() => {
    vi.mocked(api.batchRecordProgress).mockReset()
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

  it('attemptSubmit rejects client-side when a row is out of 0-100 range, without calling the API', async () => {
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '150'))

    await act(async () => {
      await result.current.attemptSubmit()
    })

    expect(result.current.validationError).toContain('0.00 ถึง 100.00')
    expect(api.batchRecordProgress).not.toHaveBeenCalled()
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
    expect(api.batchRecordProgress).not.toHaveBeenCalled()
  })

  it('confirming the decrease submits the real batch call and clears the grid on success', async () => {
    vi.mocked(api.batchRecordProgress).mockResolvedValueOnce({ entriesRecorded: 2 })
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

    expect(api.batchRecordProgress).toHaveBeenCalledWith('project-1', {
      periodEndDate: '2026-07-27T00:00:00.000Z',
      entries: [
        { activityId: activities[0].id, progressPercentage: '20', actualQuantity: null },
        { activityId: activities[1].id, progressPercentage: '60.00', actualQuantity: null },
      ],
    })
    expect(result.current.rows).toHaveLength(0)
    expect(result.current.lastResultCount).toBe(2)
    expect(result.current.pendingConfirmation).toBe(false)
  })

  it('submits directly (no confirmation) when nothing decreased, and multiple activities go in one batch call', async () => {
    vi.mocked(api.batchRecordProgress).mockResolvedValueOnce({ entriesRecorded: 2 })
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))
    act(() => result.current.updateRowProgress(activities[0].id, '55')) // 40 -> 55, an increase

    await act(async () => {
      await result.current.attemptSubmit()
    })

    expect(result.current.pendingConfirmation).toBe(false)
    expect(api.batchRecordProgress).toHaveBeenCalledTimes(1)
    expect(vi.mocked(api.batchRecordProgress).mock.calls[0][1].entries).toHaveLength(2)
  })

  it('surfaces a Thai error and keeps the rows when the server rejects the batch', async () => {
    vi.mocked(api.batchRecordProgress).mockRejectedValueOnce(
      new api.WbsApiError('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง', 400),
    )
    const { result } = renderHook(() => useBatchProgressForm('project-1'))
    act(() => result.current.addActivities(activities))
    act(() => result.current.setPeriodEndDate('2026-07-27'))

    await act(async () => {
      await result.current.attemptSubmit()
    })

    await waitFor(() => expect(result.current.submitError).toContain('รหัสกิจกรรม'))
    expect(result.current.rows).toHaveLength(2) // not cleared on failure
  })
})
