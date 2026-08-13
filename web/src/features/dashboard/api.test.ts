import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getDashboard } from './api'
import { apiClient } from '../../services/apiClient'
import type { DashboardResponseDto } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn() },
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

const sampleDashboard: DashboardResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pv: '285180000.00',
  ev: '262970000.00',
  ac: '253100000.00',
  sv: '-22210000.00',
  cv: '9870000.00',
  spi: '0.920000',
  cpi: '1.038995',
  actualCostEntryCount: 42,
  eacVariant: 'CpiBased',
  performanceFactor: '0.962477',
  etc: '213900000.00',
  eac: '466000000.00',
  vac: '19000000.00',
  eacComputable: true,
  eacNullReason: null,
  progressRollup: {
    progressPercentage: '54.20',
    weightWarnings: [],
    mixedScopeWbsNodeIds: [],
  },
  warnings: [],
  cumulativeDisbursement: '0.00',
  cumulativeWeatherStoppageDays: 0,
}

describe('features/dashboard/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
  })

  describe('getDashboard', () => {
    it('fetches the real dashboard endpoint for the given project with no dataDate override', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleDashboard })

      const result = await getDashboard('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/dashboard', {
        params: { dataDate: undefined },
      })
      expect(result).toEqual(sampleDashboard)
    })

    it('translates DashboardProjectNotFound to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'DashboardProjectNotFound' }),
      )

      await expect(getDashboard('missing')).rejects.toMatchObject({
        name: 'DashboardApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('a bodyless 403 (real ASP.NET Core role-forbid shape) still becomes a Thai error, never a raw/thrown Axios error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(403, ''))

      await expect(getDashboard('project-1')).rejects.toMatchObject({
        name: 'DashboardApiError',
        message: 'โหลดข้อมูล Dashboard ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: 403,
      })
    })

    it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
      const config = makeConfig('/projects/project-1/dashboard')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

      await expect(getDashboard('project-1')).rejects.toMatchObject({
        name: 'DashboardApiError',
        message: 'โหลดข้อมูล Dashboard ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })
})
