import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEotEvaluation } from './useEotEvaluation'
import * as api from './api'
import type { EotEvaluationDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, evaluateEot: vi.fn() }
})

const sampleEvaluation: EotEvaluationDto = {
  id: 'eval-1',
  projectId: 'project-1',
  windowStart: '2026-07-01T00:00:00+07:00',
  windowEnd: '2026-07-31T00:00:00+07:00',
  evaluatedAt: '2026-08-01T00:00:00+07:00',
  evaluatedByUserId: 'user-1',
  criticalityBasis: 'Contemporaneous',
  confidence: 'Substantiated',
  asScheduledDurationDays: 15,
  impactedDurationDays: 16,
  eotEligibleDays: 1,
  countableStoppageDayCount: 1,
  distinctCountableDateCount: 1,
  unattributedStoppageDayCount: 0,
  concurrencyAssessed: false,
  entitlementBasisAssessed: false,
  latestNoticeDate: null,
  noticeWindowExpired: null,
  runs: [],
  sources: [],
  drivers: [],
}

describe('useEotEvaluation', () => {
  beforeEach(() => {
    vi.mocked(api.evaluateEot).mockReset()
  })

  it('never calls the API on mount — evaluation is explicit-only', () => {
    renderHook(() => useEotEvaluation('project-1'))
    expect(api.evaluateEot).not.toHaveBeenCalled()
  })

  it('starts idle with no result', () => {
    const { result } = renderHook(() => useEotEvaluation('project-1'))
    expect(result.current.state).toBe('idle')
    expect(result.current.result).toBeNull()
  })

  it('evaluate() calls the API and stores the result on success', async () => {
    vi.mocked(api.evaluateEot).mockResolvedValueOnce(sampleEvaluation)
    const { result } = renderHook(() => useEotEvaluation('project-1'))

    await act(async () => {
      await result.current.evaluate()
    })

    expect(api.evaluateEot).toHaveBeenCalledWith('project-1', undefined)
    expect(result.current.state).toBe('success')
    expect(result.current.result).toEqual(sampleEvaluation)
  })

  it('evaluate() surfaces a Thai error and keeps result null on failure', async () => {
    vi.mocked(api.evaluateEot).mockRejectedValueOnce(new api.WeatherApiError('โครงการยังไม่มีประวัติการคำนวณ CPM'))
    const { result } = renderHook(() => useEotEvaluation('project-1'))

    await act(async () => {
      await result.current.evaluate()
    })

    expect(result.current.state).toBe('error')
    expect(result.current.error).toBe('โครงการยังไม่มีประวัติการคำนวณ CPM')
    expect(result.current.result).toBeNull()
  })

  it('a subsequent successful evaluate() replaces the previous result (always a new record, never a patch)', async () => {
    vi.mocked(api.evaluateEot).mockResolvedValueOnce(sampleEvaluation)
    const { result } = renderHook(() => useEotEvaluation('project-1'))
    await act(async () => {
      await result.current.evaluate()
    })

    const second = { ...sampleEvaluation, id: 'eval-2', eotEligibleDays: 0 }
    vi.mocked(api.evaluateEot).mockResolvedValueOnce(second)
    await act(async () => {
      await result.current.evaluate()
    })

    expect(result.current.result).toEqual(second)
  })
})
