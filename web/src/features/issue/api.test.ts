import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { advanceIssueStatus, createIssue, IssueApiError, listIssues } from './api'
import { apiClient } from '../../services/apiClient'
import type { CreateIssuePayload, IssueListResultDto, IssueLogDto } from './types'

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

const sampleIssue: IssueLogDto = {
  id: 'issue-1',
  projectId: 'project-1',
  sequenceNo: 24,
  title: 'น้ำรั่วซึมผนัง Basement โซน B',
  detail: 'พบคราบน้ำหลังฝนตกหนัก 8 ก.ค.',
  owner: 'วิศวกรโครงสร้าง',
  dueDate: '2026-07-18T00:00:00+07:00',
  status: 'Open',
  startedAt: null,
  closedAt: null,
  createdByUserId: 'user-1',
  createdAt: '2026-07-08T00:00:00+07:00',
}

const sampleResult: IssueListResultDto = {
  items: [sampleIssue],
  totalCount: 1,
  statusCounts: { open: 1, doing: 0, closed: 0 },
}

describe('features/issue/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('listIssues', () => {
    it('fetches the project-scoped issue register (items + totalCount + statusCounts in one call)', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleResult })

      const result = await listIssues('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/issues')
      expect(result).toEqual(sampleResult)
    })

    it('translates an unmapped error to the generic Thai message', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(500, { detail: 'SomethingElse' }))
      await expect(listIssues('project-1')).rejects.toMatchObject({ message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง' })
    })
  })

  describe('createIssue', () => {
    const payload: CreateIssuePayload = { title: 'ปัญหาใหม่', detail: null, owner: null, dueDate: null }

    it('posts to the issues endpoint and returns the created issue', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleIssue })

      const result = await createIssue('project-1', payload)

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/issues', payload)
      expect(result).toEqual(sampleIssue)
    })

    it('translates an actor-required 401', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(401, { detail: 'IssueLogActorRequired' }))
      await expect(createIssue('project-1', payload)).rejects.toBeInstanceOf(IssueApiError)

      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(401, { detail: 'IssueLogActorRequired' }))
      await expect(createIssue('project-1', payload)).rejects.toMatchObject({
        message: expect.stringContaining('เข้าสู่ระบบใหม่'),
        status: 401,
      })
    })
  })

  describe('advanceIssueStatus', () => {
    it('posts to the advance-status sub-route with no body', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleIssue, status: 'Doing' } })

      const result = await advanceIssueStatus('project-1', 'issue-1')

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/issues/issue-1/advance-status')
      expect(result.status).toBe('Doing')
    })

    it('translates IssueAlreadyClosed (terminal state, no further advance)', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(409, { detail: 'IssueAlreadyClosed' }))
      await expect(advanceIssueStatus('project-1', 'issue-1')).rejects.toMatchObject({
        message: 'ปัญหานี้ปิดแล้ว ไม่สามารถเลื่อนสถานะต่อได้',
        status: 409,
      })
    })

    it('translates a concurrent-transition 409 by its type slug when detail is unmapped', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, { detail: 'IssueLogConcurrencyConflict', type: 'https://cmplus.dev/problems/concurrent-transition' }),
      )
      await expect(advanceIssueStatus('project-1', 'issue-1')).rejects.toMatchObject({
        message: expect.stringContaining('โหลดข้อมูลใหม่'),
      })
    })
  })
})
