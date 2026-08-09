import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getEvm, listEvmSnapshots, setEacVariantDefault } from './api'
import { apiClient } from '../../services/apiClient'
import type { EvmResponseDto, EvmSnapshotDto, SetEacVariantDefaultResult } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn() },
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

const sampleEvm: EvmResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-06-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '400000.00',
  ev: '300000.00',
  ac: '350000.00',
  sv: '-100000.00',
  cv: '-50000.00',
  spi: '0.750000',
  cpi: '0.857143',
  tcpiBac: '1.076923',
  tcpiEac: '0.857143',
  selectedVariant: 'CpiBased',
  variants: [
    {
      variant: 'CpiBased',
      performanceFactor: '1.166667',
      etc: '816666.67',
      eac: '1166666.67',
      vac: '-166666.67',
      computable: true,
      reason: null,
    },
    {
      variant: 'Atypical',
      performanceFactor: '1.000000',
      etc: '700000.00',
      eac: '1050000.00',
      vac: '-50000.00',
      computable: true,
      reason: null,
    },
    {
      variant: 'CpiSpiBased',
      performanceFactor: '1.555556',
      etc: '1088888.89',
      eac: '1438888.89',
      vac: '-438888.89',
      computable: true,
      reason: null,
    },
    {
      variant: 'BottomUpEtc',
      performanceFactor: null,
      etc: null,
      eac: null,
      vac: null,
      computable: false,
      reason: 'ManualEtcNotSet',
    },
    {
      variant: 'CustomPf',
      performanceFactor: null,
      etc: null,
      eac: null,
      vac: null,
      computable: false,
      reason: 'CustomPfNotSet',
    },
  ],
  warnings: [],
}

describe('features/evm/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.put).mockReset()
  })

  describe('getEvm', () => {
    it('fetches the real evm endpoint for the given project with no eacVariant override', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleEvm })

      const result = await getEvm('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/evm', {
        params: { dataDate: undefined },
      })
      expect(result).toEqual(sampleEvm)
    })

    it('translates EvmProjectNotFound to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'EvmProjectNotFound' }),
      )

      await expect(getEvm('missing')).rejects.toMatchObject({
        name: 'EvmApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('translates the dynamic invalid-eac-variant suffix code without leaking the raw value', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/invalid-eac-variant',
          detail: 'EvmInvalidEacVariant: NotARealVariant',
        }),
      )

      await expect(getEvm('project-1')).rejects.toMatchObject({
        name: 'EvmApiError',
        message: 'ตัวแปร EAC ที่ระบุไม่ถูกต้อง',
        status: 400,
      })
    })

    it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
      const config = makeConfig('/projects/project-1/evm')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

      await expect(getEvm('project-1')).rejects.toMatchObject({
        name: 'EvmApiError',
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })

  describe('listEvmSnapshots', () => {
    const sampleSnapshot: EvmSnapshotDto = {
      snapshotId: 'snap-1',
      projectId: 'project-1',
      dataDate: '2026-04-30T00:00:00+07:00',
      bac: '1000000.00',
      pv: '200000.00',
      ev: '180000.00',
      ac: '190000.00',
      eacVariant: 'CpiBased',
      performanceFactor: '1.055556',
      eac: '1055555.56',
      etc: '865555.56',
      vac: '-55555.56',
      createdAt: '2026-05-01T09:00:00+07:00',
    }

    it('fetches the closed-period history with optional from/to bounds', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleSnapshot] })

      const result = await listEvmSnapshots('project-1', { from: '2026-01-01T00:00:00+07:00' })

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/evm/snapshots', {
        params: { from: '2026-01-01T00:00:00+07:00', to: undefined },
      })
      expect(result).toEqual([sampleSnapshot])
    })

    it('translates an inverted range (EvmInvalidSnapshotRange) to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/evm-invalid-snapshot-range',
          detail: 'EvmInvalidSnapshotRange',
        }),
      )

      await expect(listEvmSnapshots('project-1', { from: 'b', to: 'a' })).rejects.toMatchObject({
        name: 'EvmApiError',
        message: 'ช่วงวันที่ไม่ถูกต้อง (วันที่เริ่มต้นต้องไม่มากกว่าวันที่สิ้นสุด)',
        status: 400,
      })
    })
  })

  describe('setEacVariantDefault', () => {
    it('sends the variant as the request body and returns the persisted default', async () => {
      const resultDto: SetEacVariantDefaultResult = { projectId: 'project-1', eacVariantDefault: 'CpiSpiBased' }
      vi.mocked(apiClient.put).mockResolvedValueOnce({ data: resultDto })

      const result = await setEacVariantDefault('project-1', 'CpiSpiBased')

      expect(apiClient.put).toHaveBeenCalledWith('/projects/project-1/eac-default', {
        variant: 'CpiSpiBased',
      })
      expect(result).toEqual(resultDto)
    })

    it('a bodyless 403 (real ASP.NET Core role-forbid shape — no custom result handler registered) still surfaces a specific Thai permission message', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(makeError(403, ''))

      await expect(setEacVariantDefault('project-1', 'Atypical')).rejects.toMatchObject({
        name: 'EvmApiError',
        message: 'คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้',
        status: 403,
      })
    })
  })
})
