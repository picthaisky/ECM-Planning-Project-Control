import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useIssues } from './useIssues'
import * as api from './api'
import type { IssueListResultDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, listIssues: vi.fn() }
})

const sampleResult: IssueListResultDto = {
  items: [
    {
      id: 'issue-1',
      projectId: 'project-1',
      sequenceNo: 1,
      title: 'ปัญหา 1',
      detail: null,
      owner: null,
      dueDate: null,
      status: 'Open',
      startedAt: null,
      closedAt: null,
      createdByUserId: 'user-1',
      createdAt: '2026-07-08T00:00:00Z',
    },
  ],
  totalCount: 1,
  statusCounts: { open: 1, doing: 0, closed: 0 },
}

describe('useIssues', () => {
  beforeEach(() => {
    vi.mocked(api.listIssues).mockReset()
  })

  it('loads on mount and exposes the whole atomic result (items + totalCount + statusCounts together)', async () => {
    vi.mocked(api.listIssues).mockResolvedValueOnce(sampleResult)

    const { result } = renderHook(() => useIssues('project-1'))
    expect(result.current.loadState).toBe('loading')
    // Never a fabricated non-zero total while still loading.
    expect(result.current.result).toEqual({ items: [], totalCount: 0, statusCounts: { open: 0, doing: 0, closed: 0 } })

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.result).toEqual(sampleResult)
  })

  it('surfaces a Thai error message and keeps an all-zero result on failure', async () => {
    vi.mocked(api.listIssues).mockRejectedValueOnce(new api.IssueApiError('โหลดรายการปัญหาไม่สำเร็จ'))

    const { result } = renderHook(() => useIssues('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดรายการปัญหาไม่สำเร็จ')
    expect(result.current.result.totalCount).toBe(0)
  })

  it('reload() replaces the whole result atomically', async () => {
    vi.mocked(api.listIssues).mockResolvedValueOnce(sampleResult)
    const { result } = renderHook(() => useIssues('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const updated: IssueListResultDto = { ...sampleResult, totalCount: 2, statusCounts: { open: 1, doing: 1, closed: 0 } }
    vi.mocked(api.listIssues).mockResolvedValueOnce(updated)
    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.result).toEqual(updated)
  })
})
