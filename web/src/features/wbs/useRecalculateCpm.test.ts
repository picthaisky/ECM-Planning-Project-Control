import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useRecalculateCpm } from './useRecalculateCpm'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { RecalculateCpmResult } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, recalculateCpm: vi.fn() }
})

const sampleResult: RecalculateCpmResult = {
  activitiesProcessed: 4,
  criticalActivityCount: 3,
  projectDurationDays: 15,
  criticalPath: ['activity-a', 'activity-c', 'activity-d'],
}

describe('useRecalculateCpm', () => {
  beforeEach(() => {
    vi.mocked(api.recalculateCpm).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  it('starts idle, with no result and no error', () => {
    const { result } = renderHook(() => useRecalculateCpm('project-1'))
    expect(result.current.state).toBe('idle')
    expect(result.current.result).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('goes calculating -> success, stores the result, and raises a success toast naming the counts', async () => {
    let resolvePromise!: (value: RecalculateCpmResult) => void
    vi.mocked(api.recalculateCpm).mockReturnValueOnce(
      new Promise((resolve) => {
        resolvePromise = resolve
      }),
    )

    const { result } = renderHook(() => useRecalculateCpm('project-1'))

    let recalculatePromise!: Promise<RecalculateCpmResult | null>
    act(() => {
      recalculatePromise = result.current.recalculate()
    })
    expect(result.current.state).toBe('calculating')

    await act(async () => {
      resolvePromise(sampleResult)
      await recalculatePromise
    })

    expect(result.current.state).toBe('success')
    expect(result.current.result).toEqual(sampleResult)
    expect(result.current.error).toBeNull()
    expect(api.recalculateCpm).toHaveBeenCalledWith('project-1')

    await waitFor(() => expect(useToastStore.getState().toasts).toHaveLength(1))
    const [toast] = useToastStore.getState().toasts
    expect(toast.message).toContain('4')
    expect(toast.message).toContain('3')
  })

  it('goes calculating -> error and surfaces the WbsApiError Thai message, without pushing a toast', async () => {
    vi.mocked(api.recalculateCpm).mockRejectedValueOnce(
      new api.WbsApiError('พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle) ไม่สามารถคำนวณตารางเวลาได้', 422),
    )

    const { result } = renderHook(() => useRecalculateCpm('project-1'))

    await act(async () => {
      await result.current.recalculate()
    })

    expect(result.current.state).toBe('error')
    expect(result.current.result).toBeNull()
    expect(result.current.error).toContain('วนซ้ำ')
    expect(useToastStore.getState().toasts).toHaveLength(0)
  })

  it('falls back to a generic Thai message for a non-WbsApiError rejection', async () => {
    vi.mocked(api.recalculateCpm).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useRecalculateCpm('project-1'))

    await act(async () => {
      await result.current.recalculate()
    })

    expect(result.current.state).toBe('error')
    expect(result.current.error).toBe('คำนวณ CPM ใหม่ไม่สำเร็จ')
  })
})
