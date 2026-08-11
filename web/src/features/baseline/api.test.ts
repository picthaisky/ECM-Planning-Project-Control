import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { activateBaseline, BASELINE_NO_ACTIVE_BASELINE_CODE, captureBaseline, compareBaseline, listBaselines } from './api'
import { apiClient } from '../../services/apiClient'
import type { ActivateBaselineResultDto, BaselineComparisonDto, BaselineDto } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn() },
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

const sampleBaseline: BaselineDto = {
  id: 'baseline-1',
  projectId: 'project-1',
  name: 'Baseline 1 - อนุมัติสัญญา',
  isActive: false,
  capturedAt: '2026-08-01T09:00:00+07:00',
  capturedByUserId: 'user-1',
  bac: '1000000.00',
  activityCount: 250,
}

describe('features/baseline/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('listBaselines', () => {
    it('fetches the baseline list (the intended, not-yet-real shape)', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleBaseline] })

      const result = await listBaselines('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/baselines')
      expect(result).toEqual([sampleBaseline])
    })

    it('a 404 (the real backend today) throws a BaselineApiError — the caller decides how to degrade', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(404, {}))

      await expect(listBaselines('project-1')).rejects.toMatchObject({ name: 'BaselineApiError', status: 404 })
    })
  })

  describe('captureBaseline', () => {
    it('POSTs the name and returns the new baseline', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleBaseline })

      const result = await captureBaseline('project-1', 'Baseline 1 - อนุมัติสัญญา')

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/baselines', {
        name: 'Baseline 1 - อนุมัติสัญญา',
      })
      expect(result).toEqual(sampleBaseline)
    })

    it('a bodyless 403 maps to a specific Thai permission message', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(403, undefined))

      await expect(captureBaseline('project-1', 'x')).rejects.toMatchObject({
        name: 'BaselineApiError',
        status: 403,
        message: 'คุณไม่มีสิทธิ์บันทึกหรือเปิดใช้งาน Baseline สำหรับโครงการนี้',
      })
    })
  })

  describe('activateBaseline', () => {
    it('POSTs to the activate route and returns the result', async () => {
      const result: ActivateBaselineResultDto = { id: 'baseline-1', projectId: 'project-1', isActive: true }
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: result })

      const response = await activateBaseline('project-1', 'baseline-1')

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/baselines/baseline-1/activate')
      expect(response).toEqual(result)
    })
  })

  describe('compareBaseline', () => {
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

    it('GETs with no baselineId by default (server defaults to the active baseline)', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: comparison })

      const result = await compareBaseline('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/baselines/compare', {
        params: { baselineId: undefined },
      })
      expect(result).toEqual(comparison)
    })

    it('GETs with an explicit baselineId when supplied', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: comparison })

      await compareBaseline('project-1', { baselineId: 'baseline-2' })

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/baselines/compare', {
        params: { baselineId: 'baseline-2' },
      })
    })

    it('carries the stable BaselineNoActiveBaseline code for callers to branch on (not the Thai message text)', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(422, {
          type: 'https://cmplus.dev/problems/baseline-no-active-baseline',
          detail: 'BaselineNoActiveBaseline',
        }),
      )

      await expect(compareBaseline('project-1')).rejects.toMatchObject({
        name: 'BaselineApiError',
        code: BASELINE_NO_ACTIVE_BASELINE_CODE,
        status: 422,
      })
    })
  })
})
