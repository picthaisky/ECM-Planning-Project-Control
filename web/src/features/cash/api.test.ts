import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getCashFlow } from './api'
import { apiClient } from '../../services/apiClient'
import type { CashFlowResponseDto } from './types'

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

const sampleCashFlow: CashFlowResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pvCumulative: '285180000.00',
  evCumulative: '262970000.00',
  acCumulative: '253100000.00',
  actualCostEntryCount: 42,
  periods: [],
  receipts: { isAvailable: false, cumulative: null, periods: [], unavailableReason: 'PaymentCertificatesNotYetImplemented' },
  netCashPosition: null,
  warnings: [],
}

describe('features/cash/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
  })

  describe('getCashFlow', () => {
    it('fetches the real cash-flow endpoint for the given project with no dataDate/from override', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleCashFlow })

      const result = await getCashFlow('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/cash-flow', {
        params: { dataDate: undefined, from: undefined },
      })
      expect(result).toEqual(sampleCashFlow)
    })

    it('passes dataDate/from through when supplied', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleCashFlow })

      await getCashFlow('project-1', { dataDate: '2026-07-11T00:00:00+07:00', from: '2026-01-01T00:00:00+07:00' })

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/cash-flow', {
        params: { dataDate: '2026-07-11T00:00:00+07:00', from: '2026-01-01T00:00:00+07:00' },
      })
    })

    it('translates CashFlowProjectNotFound to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'CashFlowProjectNotFound' }),
      )

      await expect(getCashFlow('missing')).rejects.toMatchObject({
        name: 'CashFlowApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('translates CashFlowInvalidRange to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(400, { type: 'https://cmplus.dev/problems/bad-request', detail: 'CashFlowInvalidRange' }),
      )

      await expect(getCashFlow('project-1', { from: 'later-than-data-date' })).rejects.toMatchObject({
        name: 'CashFlowApiError',
        message: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้นต้องไม่มากกว่าวันที่ข้อมูลปัจจุบัน)',
        status: 400,
      })
    })

    it('a bodyless 403 (real ASP.NET Core role-forbid shape) still becomes a Thai error, never a raw/thrown Axios error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(403, ''))

      await expect(getCashFlow('project-1')).rejects.toMatchObject({
        name: 'CashFlowApiError',
        message: 'โหลดข้อมูล Cash Flow ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: 403,
      })
    })

    it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
      const config = makeConfig('/projects/project-1/cash-flow')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

      await expect(getCashFlow('project-1')).rejects.toMatchObject({
        name: 'CashFlowApiError',
        message: 'โหลดข้อมูล Cash Flow ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })
})
