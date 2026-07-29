import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { batchRecordProgress, getNodeActivities, getWbsTree, WbsApiError } from './api'
import { apiClient } from '../../services/apiClient'
import type { WbsTreeDto } from './types'

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

const sampleTree: WbsTreeDto = {
  projectId: 'project-1',
  rootNodes: [
    {
      id: 'root-1',
      parentWbsNodeId: null,
      code: '1',
      title: 'งานโครงสร้าง',
      weightPercentage: '38.00',
      activityCount: 2,
      children: [],
    },
  ],
}

describe('features/wbs/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('getWbsTree', () => {
    it('fetches the real wbs-tree endpoint', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleTree })

      const result = await getWbsTree('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/wbs-tree')
      expect(result).toEqual(sampleTree)
    })

    it('translates WbsProjectNotFound to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(404, { detail: 'WbsProjectNotFound' }))

      await expect(getWbsTree('missing')).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ไม่พบโครงการที่ระบุ',
      })
    })

    it('a genuine network failure (no HTTP response at all — offline/DNS/timeout) becomes the generic Thai error, not a raw/thrown Axios error', async () => {
      // Distinct from every other rejection test in this file: those all construct an AxiosError
      // *with* a `.response` (a real server reply, just a bad status). A true network failure
      // (no connectivity, request never reached a server) is an AxiosError with `response`
      // `undefined` - `toWbsApiError`'s `problem`/`typeSlug`/`code` all resolve to nothing in that
      // case, so it must fall through to the generic message with no `status`, never leak the raw
      // Axios "Network Error" text to a Thai-first UI.
      const config = makeConfig('/projects/project-1/wbs-tree')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

      await expect(getWbsTree('project-1')).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })

  describe('getNodeActivities (pending backend endpoint)', () => {
    it('requests the node-scoped activities path', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [] })

      await getNodeActivities('project-1', 'node-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/wbs-nodes/node-1/activities')
    })

    it('surfaces a real 404 (endpoint not shipped yet) as a Thai WbsApiError, never a hang or a thrown raw error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(404, {}))

      await expect(getNodeActivities('project-1', 'node-1')).rejects.toBeInstanceOf(WbsApiError)
    })
  })

  describe('batchRecordProgress', () => {
    it('posts the batch to the real endpoint and returns the entriesRecorded summary', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { entriesRecorded: 2 } })

      const result = await batchRecordProgress('project-1', {
        periodEndDate: '2026-07-27T00:00:00.000Z',
        entries: [{ activityId: 'a1', progressPercentage: '50.00', actualQuantity: null }],
      })

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/progress/batch', {
        periodEndDate: '2026-07-27T00:00:00.000Z',
        entries: [{ activityId: 'a1', progressPercentage: '50.00', actualQuantity: null }],
      })
      expect(result).toEqual({ entriesRecorded: 2 })
    })

    it('translates ProgressUnknownActivity to a Thai error (verified against the real API shape)', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/progress-unknown-activity',
          detail: 'ProgressUnknownActivity',
        }),
      )

      await expect(
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }),
      ).rejects.toMatchObject({
        message: 'พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง',
      })
    })

    // Verified directly against the real, live backend during this sprint's e2e check: a
    // FluentValidation failure on this Result<T>-shaped command surfaces as
    // `type=".../problems/bad-request"` with `detail` set to the *raw English* FluentValidation
    // message (e.g. "'Progress Percentage' must be between 0 and 100. You entered 150.00.") —
    // never `"validation-error"`. This must never reach the Thai-first UI verbatim.
    it('translates the real "bad-request" fallback bucket to a generic Thai message, never leaking the raw English FluentValidation text', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/bad-request',
          title: 'The request could not be completed.',
          detail: "'Progress Percentage' must be between 0 and 100. You entered 150.00.",
        }),
      )

      await expect(
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }),
      ).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
        status: 400,
      })
    })

    it('a genuine network failure on submit (no HTTP response) becomes the generic Thai error, so the UI never has to distinguish "offline" from "unknown failure"', async () => {
      const config = makeConfig('/projects/project-1/progress/batch')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.post).mockRejectedValueOnce(networkError)

      await expect(
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }),
      ).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })
})
