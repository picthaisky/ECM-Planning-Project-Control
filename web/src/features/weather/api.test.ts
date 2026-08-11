import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { evaluateEot, listWeatherLogs, recordWeatherLog, recordWeatherLogCorrection, WeatherApiError } from './api'
import { apiClient } from '../../services/apiClient'
import type { EotEvaluationDto, RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload, WeatherLogDto } from './types'

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

const sampleLog: WeatherLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  logDate: '2026-07-08T00:00:00+07:00',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '61.00',
  impact: 'FullStoppage',
  impactNote: 'หยุดงานภายนอกทั้งวัน',
  hoursLost: '8.00',
  workStoppage: true,
  entryKind: 'Original',
  correctsWeatherLogId: null,
  correctionReason: null,
  affectedActivityIds: ['activity-1'],
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-08T09:00:00+07:00',
}

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

describe('features/weather/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('listWeatherLogs', () => {
    it('fetches the project-scoped weather-log register with optional from/to', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleLog] })

      const result = await listWeatherLogs('project-1', { from: '2026-07-01T00:00:00Z' })

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/weather-logs', {
        params: { from: '2026-07-01T00:00:00Z', to: undefined },
      })
      expect(result).toEqual([sampleLog])
    })

    it('translates an unmapped error to the generic Thai message', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(500, { detail: 'SomethingElse' }))
      await expect(listWeatherLogs('project-1')).rejects.toMatchObject({
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      })
    })
  })

  describe('recordWeatherLog', () => {
    const payload: RecordWeatherLogPayload = {
      logDate: '2026-07-08T00:00:00.000Z',
      condition: 'HeavyRain',
      conditionNote: null,
      rainfallMm: '61.00',
      impact: 'FullStoppage',
      impactNote: null,
      hoursLost: '8.00',
      affectedActivityIds: [],
    }

    it('posts to the weather-logs endpoint and returns the created entry', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleLog })

      const result = await recordWeatherLog('project-1', payload)

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/weather-logs', payload)
      expect(result).toEqual(sampleLog)
    })

    it('translates WeatherLogUnknownActivity by its detail code', async () => {
      const problem = { detail: 'WeatherLogUnknownActivity', type: 'https://cmplus.dev/problems/weather-log-unknown-activity' }
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(400, problem))
      await expect(recordWeatherLog('project-1', payload)).rejects.toBeInstanceOf(WeatherApiError)

      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(400, problem))
      await expect(recordWeatherLog('project-1', payload)).rejects.toMatchObject({
        message: expect.stringContaining('รหัสกิจกรรม'),
        status: 400,
      })
    })

    it('falls back to the type slug when detail is not in the table', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(405, { detail: 'SomeUnknownDetail', type: 'https://cmplus.dev/problems/weather-log-is-immutable' }),
      )
      await expect(recordWeatherLog('project-1', payload)).rejects.toMatchObject({
        message: expect.stringContaining('บันทึกรายการแก้ไขใหม่แทน'),
      })
    })
  })

  describe('recordWeatherLogCorrection', () => {
    const payload: RecordWeatherLogCorrectionPayload = {
      entryKind: 'Correction',
      correctionReason: 'ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง',
      logDate: '2026-07-08T00:00:00.000Z',
      condition: 'HeavyRain',
      conditionNote: null,
      rainfallMm: '61.00',
      impact: 'FullStoppage',
      impactNote: null,
      hoursLost: '3.00',
      affectedActivityIds: [],
    }

    it('posts to the corrections sub-route of the target log id', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleLog, id: 'log-2', entryKind: 'Correction' } })

      const result = await recordWeatherLogCorrection('project-1', 'log-1', payload)

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/weather-logs/log-1/corrections', payload)
      expect(result.entryKind).toBe('Correction')
    })

    it('translates WeatherLogAlreadySuperseded (the load-bearing chain-tail error)', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(409, { detail: 'WeatherLogAlreadySuperseded' }))
      await expect(recordWeatherLogCorrection('project-1', 'log-1', payload)).rejects.toMatchObject({
        message: expect.stringContaining('โหลดข้อมูลใหม่'),
        status: 409,
      })
    })
  })

  describe('evaluateEot', () => {
    it('posts an empty-window body by default', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleEvaluation })

      const result = await evaluateEot('project-1')

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/eot-evaluations', {
        windowStart: null,
        windowEnd: null,
      })
      expect(result).toEqual(sampleEvaluation)
    })

    it('translates EotNoCpmRunAvailable with an actionable Thai message', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(422, { detail: 'EotNoCpmRunAvailable' }))
      await expect(evaluateEot('project-1')).rejects.toMatchObject({
        message: expect.stringContaining('คำนวณ CPM'),
        status: 422,
      })
    })

    it('translates EotProjectCalendarNotConfigured', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(422, { detail: 'EotProjectCalendarNotConfigured' }))
      await expect(evaluateEot('project-1')).rejects.toMatchObject({
        message: expect.stringContaining('ปฏิทินการทำงาน'),
      })
    })
  })
})
