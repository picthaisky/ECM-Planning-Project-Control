import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useIssueActions } from './useIssueActions'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { CreateIssuePayload, IssueLogDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, createIssue: vi.fn(), advanceIssueStatus: vi.fn() }
})

const sampleIssue: IssueLogDto = {
  id: 'issue-1',
  projectId: 'project-1',
  sequenceNo: null,
  title: 'ปัญหาใหม่',
  detail: null,
  owner: null,
  dueDate: null,
  status: 'Open',
  startedAt: null,
  closedAt: null,
  createdByUserId: 'user-1',
  createdAt: '2026-07-08T00:00:00Z',
}

describe('useIssueActions', () => {
  beforeEach(() => {
    vi.mocked(api.createIssue).mockReset()
    vi.mocked(api.advanceIssueStatus).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  describe('create', () => {
    const payload: CreateIssuePayload = { title: 'ปัญหาใหม่', detail: null, owner: null, dueDate: null }

    it('calls the API, calls onSaved (a full reload — never a local splice, see this hook\'s own remarks), and toasts', async () => {
      vi.mocked(api.createIssue).mockResolvedValueOnce(sampleIssue)
      const onSaved = vi.fn()
      const { result } = renderHook(() => useIssueActions('project-1', onSaved))

      await act(async () => {
        await result.current.create(payload)
      })

      expect(api.createIssue).toHaveBeenCalledWith('project-1', payload)
      expect(onSaved).toHaveBeenCalledTimes(1)
      expect(result.current.creating).toBe(false)
      expect(useToastStore.getState().toasts).toHaveLength(1)
    })

    it('sets createError and does not call onSaved on failure', async () => {
      vi.mocked(api.createIssue).mockRejectedValueOnce(new api.IssueApiError('แจ้งปัญหาใหม่ไม่สำเร็จ'))
      const onSaved = vi.fn()
      const { result } = renderHook(() => useIssueActions('project-1', onSaved))

      await act(async () => {
        await result.current.create(payload)
      })

      expect(result.current.createError).toBe('แจ้งปัญหาใหม่ไม่สำเร็จ')
      expect(onSaved).not.toHaveBeenCalled()
    })
  })

  describe('advance', () => {
    it('calls the API with the issue id and calls onSaved on success', async () => {
      vi.mocked(api.advanceIssueStatus).mockResolvedValueOnce({ ...sampleIssue, status: 'Doing' })
      const onSaved = vi.fn()
      const { result } = renderHook(() => useIssueActions('project-1', onSaved))

      await act(async () => {
        await result.current.advance('issue-1')
      })

      expect(api.advanceIssueStatus).toHaveBeenCalledWith('project-1', 'issue-1')
      expect(onSaved).toHaveBeenCalledTimes(1)
    })

    it('tracks advancingId only while that specific request is in flight', async () => {
      let resolvePromise: (value: IssueLogDto) => void = () => {}
      vi.mocked(api.advanceIssueStatus).mockReturnValueOnce(
        new Promise((resolve) => {
          resolvePromise = resolve
        }),
      )
      const { result } = renderHook(() => useIssueActions('project-1', vi.fn()))

      let advancePromise!: Promise<IssueLogDto | null>
      act(() => {
        advancePromise = result.current.advance('issue-1')
      })
      expect(result.current.advancingId).toBe('issue-1')

      await act(async () => {
        resolvePromise({ ...sampleIssue, status: 'Doing' })
        await advancePromise
      })
      expect(result.current.advancingId).toBeNull()
    })

    it('sets advanceError on failure (e.g. IssueAlreadyClosed)', async () => {
      vi.mocked(api.advanceIssueStatus).mockRejectedValueOnce(new api.IssueApiError('ปัญหานี้ปิดแล้ว ไม่สามารถเลื่อนสถานะต่อได้'))
      const { result } = renderHook(() => useIssueActions('project-1', vi.fn()))

      await act(async () => {
        await result.current.advance('issue-1')
      })

      expect(result.current.advanceError).toBe('ปัญหานี้ปิดแล้ว ไม่สามารถเลื่อนสถานะต่อได้')
    })
  })
})
