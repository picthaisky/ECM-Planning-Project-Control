import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getProductivityIndex, ManpowerApiError, recordManpowerLog, recordManpowerLogCorrection } from './api'
import { apiClient } from '../../services/apiClient'
import type { ManpowerLogDto, ProductivityIndexResponseDto, RecordManpowerLogPayload } from './types'

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

const samplePayload: RecordManpowerLogPayload = {
  logDate: '2026-07-07T00:00:00.000Z',
  shift: 'Day',
  workCategoryId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
  wbsNodeId: null,
  activityId: null,
  labourType: 'OwnDirect',
  subcontractorRef: null,
  workerCount: 25,
  manHours: '200.00',
  overtimeHours: '0.00',
  manHoursDerived: false,
  equipmentCount: 0,
  equipmentOperatingHours: '0.00',
  equipmentStandbyHours: '0.00',
  workDescription: null,
  relatedWeatherLogId: null,
  allowDuplicate: false,
}

const sampleLog: ManpowerLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  ...samplePayload,
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-07T09:00:00.000Z',
  entryKind: 'Original',
  correctsLogId: null,
  correctionReason: null,
  allowDuplicateOverride: false,
}

const samplePiResponse: ProductivityIndexResponseDto = {
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
}

describe('features/maneq/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('recordManpowerLog', () => {
    it('posts to the manpower-logs endpoint and returns the saved row', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleLog })
      const result = await recordManpowerLog('project-1', samplePayload)
      expect(result).toEqual(sampleLog)
      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/manpower-logs', samplePayload)
    })

    it('maps ManpowerLogAlreadyExists to its Thai message', async () => {
      vi.mocked(apiClient.post).mockRejectedValue(
        makeError(409, { type: '/errors/manpower-log-already-exists', detail: 'ManpowerLogAlreadyExists' }),
      )
      await expect(recordManpowerLog('project-1', samplePayload)).rejects.toBeInstanceOf(ManpowerApiError)
      await expect(recordManpowerLog('project-1', samplePayload)).rejects.toMatchObject({
        message: expect.stringContaining('มีบันทึกสำหรับวันที่'),
      })
    })

    it('falls back to the generic Thai message for an unmapped code', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(500, { type: '/errors/x', detail: 'Unmapped' }))
      await expect(recordManpowerLog('project-1', samplePayload)).rejects.toMatchObject({
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      })
    })
  })

  describe('recordManpowerLogCorrection', () => {
    it('posts to the corrections sub-route with logId in the path', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleLog })
      await recordManpowerLogCorrection('project-1', 'log-1', {
        ...samplePayload,
        entryKind: 'Correction',
        correctionReason: 'พิมพ์ผิด',
      })
      expect(apiClient.post).toHaveBeenCalledWith(
        '/projects/project-1/manpower-logs/log-1/corrections',
        expect.objectContaining({ entryKind: 'Correction', correctionReason: 'พิมพ์ผิด' }),
      )
    })

    it('maps ManpowerLogAlreadySuperseded to its Thai message', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, { type: '/errors/manpower-log-already-superseded', detail: 'ManpowerLogAlreadySuperseded' }),
      )
      await expect(
        recordManpowerLogCorrection('project-1', 'log-1', { ...samplePayload, entryKind: 'Correction', correctionReason: 'x' }),
      ).rejects.toMatchObject({ message: expect.stringContaining('มีรายการแก้ไขอื่นอยู่แล้ว') })
    })
  })

  describe('getProductivityIndex', () => {
    it('fetches the productivity-index endpoint with the given scope/window params', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: samplePiResponse })
      const result = await getProductivityIndex('project-1', { to: '2026-07-08T00:00:00.000Z' })
      expect(result).toEqual(samplePiResponse)
      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/manpower-logs/productivity-index', {
        params: { wbsNodeId: undefined, activityId: undefined, from: undefined, to: '2026-07-08T00:00:00.000Z' },
      })
    })

    it('passes wbsNodeId/from through when provided (narrowed, single-day scope)', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: samplePiResponse })
      await getProductivityIndex('project-1', {
        wbsNodeId: 'node-1',
        from: '2026-07-07T00:00:00.000Z',
        to: '2026-07-08T00:00:00.000Z',
      })
      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/manpower-logs/productivity-index', {
        params: { wbsNodeId: 'node-1', activityId: undefined, from: '2026-07-07T00:00:00.000Z', to: '2026-07-08T00:00:00.000Z' },
      })
    })

    it('maps ManpowerLogInvalidDateRange to its Thai message', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(400, { type: '/errors/manpower-log-invalid-date-range', detail: 'ManpowerLogInvalidDateRange' }),
      )
      await expect(getProductivityIndex('project-1', { to: '2026-07-08T00:00:00.000Z' })).rejects.toMatchObject({
        message: expect.stringContaining('ช่วงวันที่ไม่ถูกต้อง'),
      })
    })
  })
})
