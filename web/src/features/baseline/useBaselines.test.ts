import { renderHook, waitFor } from '@testing-library/react'
import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useBaselines } from './useBaselines'
import * as api from './api'
import type { ActivateBaselineResultDto, BaselineDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, listBaselines: vi.fn(), captureBaseline: vi.fn(), activateBaseline: vi.fn() }
})

const baselineA: BaselineDto = {
  id: 'baseline-a',
  projectId: 'project-1',
  name: 'Baseline A',
  isActive: true,
  capturedAt: '2026-07-01T00:00:00+07:00',
  capturedByUserId: 'user-1',
  bac: '1000000.00',
  activityCount: 100,
}

describe('useBaselines', () => {
  beforeEach(() => {
    vi.mocked(api.listBaselines).mockReset()
    vi.mocked(api.captureBaseline).mockReset()
    vi.mocked(api.activateBaseline).mockReset()
  })

  it('loads the real list on mount when it succeeds, and marks listAvailable true', async () => {
    vi.mocked(api.listBaselines).mockResolvedValueOnce([baselineA])

    const { result } = renderHook(() => useBaselines('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.baselines).toEqual([baselineA])
    expect(result.current.listAvailable).toBe(true)
  })

  it('degrades gracefully (never a blocking error) when listBaselines 404s — empty session-local list, listAvailable false', async () => {
    vi.mocked(api.listBaselines).mockRejectedValueOnce(new api.BaselineApiError('ไม่พบ', 404))

    const { result } = renderHook(() => useBaselines('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.baselines).toEqual([])
    expect(result.current.listAvailable).toBe(false)
  })

  it('capture() prepends the new baseline to the (possibly session-local) list', async () => {
    vi.mocked(api.listBaselines).mockRejectedValueOnce(new api.BaselineApiError('ไม่พบ', 404))
    const created: BaselineDto = { ...baselineA, id: 'baseline-b', name: 'Baseline B', isActive: false }
    vi.mocked(api.captureBaseline).mockResolvedValueOnce(created)

    const { result } = renderHook(() => useBaselines('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    let captured: BaselineDto | null = null
    await act(async () => {
      captured = await result.current.capture('Baseline B')
    })

    expect(captured).toEqual(created)
    expect(result.current.baselines).toEqual([created])
    expect(api.captureBaseline).toHaveBeenCalledWith('project-1', 'Baseline B')
  })

  it('activate() flips isActive exclusively across the local list (only the target stays active)', async () => {
    const baselineB: BaselineDto = { ...baselineA, id: 'baseline-b', name: 'Baseline B', isActive: false }
    vi.mocked(api.listBaselines).mockResolvedValueOnce([baselineA, baselineB])
    const result_: ActivateBaselineResultDto = { id: 'baseline-b', projectId: 'project-1', isActive: true }
    vi.mocked(api.activateBaseline).mockResolvedValueOnce(result_)

    const { result } = renderHook(() => useBaselines('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    await act(async () => {
      await result.current.activate('baseline-b')
    })

    const byId = new Map(result.current.baselines.map((b) => [b.id, b]))
    expect(byId.get('baseline-a')?.isActive).toBe(false)
    expect(byId.get('baseline-b')?.isActive).toBe(true)
  })

  it('capture() failure surfaces the Thai error without touching the existing list', async () => {
    vi.mocked(api.listBaselines).mockResolvedValueOnce([baselineA])
    vi.mocked(api.captureBaseline).mockRejectedValueOnce(new api.BaselineApiError('บันทึกไม่สำเร็จ', 400))

    const { result } = renderHook(() => useBaselines('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    await act(async () => {
      await result.current.capture('x')
    })

    expect(result.current.actionState).toBe('error')
    expect(result.current.actionError).toBe('บันทึกไม่สำเร็จ')
    expect(result.current.baselines).toEqual([baselineA])
  })
})
