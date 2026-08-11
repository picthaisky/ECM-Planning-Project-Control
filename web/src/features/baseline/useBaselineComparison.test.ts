import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useBaselineComparison } from './useBaselineComparison'
import * as api from './api'
import { BASELINE_NO_ACTIVE_BASELINE_CODE } from './api'
import type { BaselineComparisonDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, compareBaseline: vi.fn() }
})

const comparison: BaselineComparisonDto = {
  projectId: 'project-1',
  baselineId: 'baseline-1',
  baselineName: 'Baseline 1',
  baselineCapturedAt: '2026-08-01T09:00:00+07:00',
  totalActivityCount: 2,
  driftedActivityCount: 1,
  projectFinishVarianceDays: 3,
  currentBac: '1000000.00',
  baselineBac: '1000000.00',
  bacVarianceAmount: '0.00',
  activities: [],
}

describe('useBaselineComparison', () => {
  beforeEach(() => {
    vi.mocked(api.compareBaseline).mockReset()
  })

  it('loads the real comparison on mount, defaulting to the active baseline (no baselineId passed)', async () => {
    vi.mocked(api.compareBaseline).mockResolvedValueOnce(comparison)

    const { result } = renderHook(() => useBaselineComparison('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.comparison).toEqual(comparison)
    expect(api.compareBaseline).toHaveBeenCalledWith('project-1', { baselineId: undefined })
  })

  it('passes an explicit baselineId through when supplied', async () => {
    vi.mocked(api.compareBaseline).mockResolvedValueOnce(comparison)

    renderHook(() => useBaselineComparison('project-1', 'baseline-2'))

    await waitFor(() =>
      expect(api.compareBaseline).toHaveBeenCalledWith('project-1', { baselineId: 'baseline-2' }),
    )
  })

  it('BaselineNoActiveBaseline gets its own load state, distinct from a generic error', async () => {
    vi.mocked(api.compareBaseline).mockRejectedValueOnce(
      new api.BaselineApiError(
        'โครงการนี้ยังไม่มี Baseline ที่ Active อยู่',
        422,
        BASELINE_NO_ACTIVE_BASELINE_CODE,
      ),
    )

    const { result } = renderHook(() => useBaselineComparison('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('no-active-baseline'))
    expect(result.current.comparison).toBeNull()
  })

  it('any other failure is a generic Thai error state', async () => {
    vi.mocked(api.compareBaseline).mockRejectedValueOnce(new api.BaselineApiError('โหลดไม่สำเร็จ', 500))

    const { result } = renderHook(() => useBaselineComparison('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดไม่สำเร็จ')
  })
})
