import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { batchRecordProgress, getNodeActivities, getWbsTree, getWbsTreeWithCacheInfo, recalculateCpm, WbsApiError } from './api'
import { apiClient } from '../../services/apiClient'
import type { RecalculateCpmResult, WbsTreeDto } from './types'

// Partial mock (`importActual` + spread) rather than replacing the whole module — `wbs/api.ts` also
// imports the real, pure `isServedFromOfflineCache` from this module (S13-FE-02) and must keep using
// it, not `undefined`.
vi.mock('../../services/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../services/apiClient')>('../../services/apiClient')
  return { ...actual, apiClient: { get: vi.fn(), post: vi.fn() } }
})

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

  describe('getWbsTreeWithCacheInfo (S13-FE-02)', () => {
    it('reports servedFromOfflineCache: false for an ordinary live response', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleTree, headers: {} })

      const result = await getWbsTreeWithCacheInfo('project-1')

      expect(result).toEqual({ tree: sampleTree, servedFromOfflineCache: false })
    })

    it('reports servedFromOfflineCache: true when the response carries the service worker\'s cache-fallback header', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleTree, headers: { 'x-cm-served-from': 'sw-cache' } })

      const result = await getWbsTreeWithCacheInfo('project-1')

      expect(result).toEqual({ tree: sampleTree, servedFromOfflineCache: true })
    })

    it('getWbsTree (back-compat wrapper) still returns the bare tree, unaffected by the cache header', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleTree, headers: { 'x-cm-served-from': 'sw-cache' } })

      const result = await getWbsTree('project-1')

      expect(result).toEqual(sampleTree)
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
    it('posts the batch to the real endpoint with the Idempotency-Key header and returns the entriesRecorded summary', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { entriesRecorded: 2 } })

      const result = await batchRecordProgress(
        'project-1',
        {
          periodEndDate: '2026-07-27T00:00:00.000Z',
          entries: [{ activityId: 'a1', progressPercentage: '50.00', actualQuantity: null }],
        },
        'idem-key-1',
      )

      expect(apiClient.post).toHaveBeenCalledWith(
        '/projects/project-1/progress/batch',
        {
          periodEndDate: '2026-07-27T00:00:00.000Z',
          entries: [{ activityId: 'a1', progressPercentage: '50.00', actualQuantity: null }],
        },
        { headers: { 'Idempotency-Key': 'idem-key-1' } },
      )
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
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }, 'idem-key-x'),
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
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }, 'idem-key-x'),
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
        batchRecordProgress('project-1', { periodEndDate: '2026-07-27T00:00:00.000Z', entries: [] }, 'idem-key-x'),
      ).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })

  describe('recalculateCpm (S5-BE-04)', () => {
    it('posts to the real recalculate endpoint and returns the critical path in schedule order', async () => {
      const sampleResult: RecalculateCpmResult = {
        activitiesProcessed: 4,
        criticalActivityCount: 3,
        projectDurationDays: 15,
        criticalPath: ['activity-a', 'activity-c', 'activity-d'],
      }
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleResult })

      const result = await recalculateCpm('project-1')

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/cpm/recalculate')
      expect(result).toEqual(sampleResult)
    })

    it('translates CpmProjectNotFound to the same Thai message as the other "project not found" codes', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(404, {
          type: 'https://cmplus.dev/problems/not-found',
          detail: 'CpmProjectNotFound',
        }),
      )

      await expect(recalculateCpm('missing')).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('translates CpmDuplicateRelation (S5-BE-03) to a Thai error naming Activity Relation', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, {
          type: 'https://cmplus.dev/problems/cpm-duplicate-relation',
          detail: 'CpmDuplicateRelation',
        }),
      )

      await expect(recalculateCpm('project-1')).rejects.toMatchObject({
        message: expect.stringContaining('Activity Relation'),
        status: 422,
      })
    })

    it('translates CpmUnknownActivityInRelation to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, {
          type: 'https://cmplus.dev/problems/cpm-unknown-activity',
          detail: 'CpmUnknownActivityInRelation',
        }),
      )

      await expect(recalculateCpm('project-1')).rejects.toMatchObject({
        message: expect.stringContaining('นอกโครงการนี้'),
        status: 422,
      })
    })

    // Verified against the real backend shape (ResultProblemMapper's remarks + CpmEngine.Calculate):
    // a rejected cycle's `ProblemDetails.detail` is the dynamic string
    // "CpmCycleDetected: A -> B -> A" (the code plus the offending chain, joined by " -> ") — never
    // an exact, stable dictionary key. The frontend must show the actual chain, not discard it.
    it('translates a CpmCycleDetected rejection into a Thai message that still shows the offending chain', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, {
          type: 'https://cmplus.dev/problems/cpm-cycle-detected',
          title: 'The activity relation graph contains a cycle and cannot be scheduled.',
          detail:
            'CpmCycleDetected: 11111111-1111-1111-1111-111111111111 -> 22222222-2222-2222-2222-222222222222 -> 11111111-1111-1111-1111-111111111111',
        }),
      )

      let caught: unknown
      try {
        await recalculateCpm('project-1')
      } catch (err) {
        caught = err
      }

      expect(caught).toMatchObject({ name: 'WbsApiError', status: 422 })
      expect((caught as WbsApiError).message).toContain('วนซ้ำ')
      expect((caught as WbsApiError).message).toContain(
        '11111111-1111-1111-1111-111111111111 → 22222222-2222-2222-2222-222222222222',
      )
    })

    it('a genuine network failure becomes the generic Thai error', async () => {
      const config = makeConfig('/projects/project-1/cpm/recalculate')
      const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
      vi.mocked(apiClient.post).mockRejectedValueOnce(networkError)

      await expect(recalculateCpm('project-1')).rejects.toMatchObject({
        name: 'WbsApiError',
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        status: undefined,
      })
    })
  })
})
