import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useManpowerOverview } from './useManpowerOverview'
import { getProductivityIndex, ManpowerApiError } from './api'
import type { ProductivityIndexResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getProductivityIndex: vi.fn() }
})

function makeResponse(overrides: Partial<ProductivityIndexResponseDto> = {}): ProductivityIndexResponseDto {
  return {
    projectId: 'project-1',
    wbsNodeId: null,
    activityId: null,
    from: null,
    to: '2026-07-08T00:00:00.000Z',
    productivityIndex: '0.90',
    productivityIndexNullReason: null,
    earnedManHours: '180.00',
    actualManHoursInScope: '200.00',
    actualManHoursTotal: '200.00',
    excludedManHours: '0.00',
    coveragePercentage: '100.00',
    logEntryCount: 1,
    warnings: [],
    manningRatio: null,
    actualWorkerCount: null,
    plannedWorkerCount: null,
    ...overrides,
  }
}

describe('useManpowerOverview', () => {
  beforeEach(() => {
    vi.mocked(getProductivityIndex).mockReset()
  })

  it('fetches cumulative (from omitted), today (single-day, with manning), month-to-date, and a 7-day histogram, each independently', async () => {
    vi.mocked(getProductivityIndex).mockImplementation(async (_projectId, params) => {
      if (params.from === null || params.from === undefined) {
        return makeResponse({ from: null, productivityIndex: '0.97' }) // cumulative
      }
      return makeResponse({ from: params.from, to: params.to, actualWorkerCount: 186, plannedWorkerCount: 205 })
    })

    const { result } = renderHook(() => useManpowerOverview('project-1'))

    await waitFor(() => expect(result.current.cumulative.state).toBe('ready'))
    await waitFor(() => expect(result.current.today.state).toBe('ready'))
    await waitFor(() => expect(result.current.monthToDate.state).toBe('ready'))
    await waitFor(() => expect(result.current.histogram.state).toBe('ready'))

    expect(result.current.cumulative.data?.productivityIndex).toBe('0.97')
    expect(result.current.today.data?.actualWorkerCount).toBe(186)
    expect(result.current.today.data?.plannedWorkerCount).toBe(205)
    expect(result.current.histogram.data).toHaveLength(7)
    // Every histogram request must be an exactly-one-day bucket (from/to 24h apart).
    for (const point of result.current.histogram.data ?? []) {
      const from = new Date(point.response.from ?? '').getTime()
      const to = new Date(point.response.to).getTime()
      expect(to - from).toBe(24 * 60 * 60 * 1000)
    }
  })

  it('cumulative failing does not block today/monthToDate/histogram from loading', async () => {
    vi.mocked(getProductivityIndex).mockImplementation(async (_projectId, params) => {
      if (params.from === null || params.from === undefined) {
        throw new ManpowerApiError('โหลดไม่สำเร็จ')
      }
      return makeResponse({ from: params.from, to: params.to })
    })

    const { result } = renderHook(() => useManpowerOverview('project-1'))

    await waitFor(() => expect(result.current.cumulative.state).toBe('error'))
    expect(result.current.cumulative.error).toBe('โหลดไม่สำเร็จ')
    await waitFor(() => expect(result.current.today.state).toBe('ready'))
    await waitFor(() => expect(result.current.monthToDate.state).toBe('ready'))
    await waitFor(() => expect(result.current.histogram.state).toBe('ready'))
  })

  it('respects a custom histogramDays count', async () => {
    vi.mocked(getProductivityIndex).mockResolvedValue(makeResponse())

    const { result } = renderHook(() => useManpowerOverview('project-1', null, 3))
    await waitFor(() => expect(result.current.histogram.state).toBe('ready'))
    expect(result.current.histogram.data).toHaveLength(3)
  })

  it('reload() re-fetches every section', async () => {
    vi.mocked(getProductivityIndex).mockResolvedValue(makeResponse())
    const { result } = renderHook(() => useManpowerOverview('project-1'))
    await waitFor(() => expect(result.current.cumulative.state).toBe('ready'))

    const callsBefore = vi.mocked(getProductivityIndex).mock.calls.length
    await result.current.reload()
    expect(vi.mocked(getProductivityIndex).mock.calls.length).toBeGreaterThan(callsBefore)
  })
})
