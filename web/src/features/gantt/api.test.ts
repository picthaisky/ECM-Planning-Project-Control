import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getGantt } from './api'
import { apiClient } from '../../services/apiClient'
import type { GanttDto } from './types'

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

const sampleGantt: GanttDto = {
  projectId: 'project-1',
  dataDate: '2026-03-15T00:00:00+07:00',
  activities: [
    {
      id: 'a1',
      wbsNodeId: 'node-1',
      activityCode: 'ACT-101',
      name: 'งานเสาเข็มและฐานราก',
      plannedStart: '2026-01-01T00:00:00+07:00',
      plannedFinish: '2026-01-16T00:00:00+07:00',
      actualStart: null,
      actualFinish: null,
      isCritical: false,
      totalFloat: 5,
      freeFloat: 5,
    },
  ],
}

describe('features/gantt/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
  })

  describe('getGantt', () => {
    it('fetches the real gantt endpoint for the given project', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleGantt })

      const result = await getGantt('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/gantt')
      expect(result).toEqual(sampleGantt)
    })

    it('translates GanttProjectNotFound to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'GanttProjectNotFound' }),
      )

      await expect(getGantt('missing')).rejects.toMatchObject({
        name: 'GanttApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('a genuine network failure (no HTTP response at all) becomes the generic Thai error, never a raw/thrown Axios error', async () => {
      const config = makeConfig('/projects/project-1/gantt')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

      await expect(getGantt('project-1')).rejects.toMatchObject({
        name: 'GanttApiError',
        message: 'โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })

    it('an unmapped error code still gets a well-formed Thai fallback, never the raw code/detail leaking to the UI', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(500, { type: 'https://cmplus.dev/problems/unexpected', detail: 'SomethingElseEntirely' }),
      )

      await expect(getGantt('project-1')).rejects.toMatchObject({
        name: 'GanttApiError',
        message: 'โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: 500,
      })
    })
  })
})
