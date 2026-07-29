import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useImportWizard } from './useImportWizard'
import * as api from './api'
import type { FileImportJob } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    importFile: vi.fn(),
    getImportJob: vi.fn(),
    getImportJobHistory: vi.fn(),
    downloadProgressTemplate: vi.fn(),
  }
})

function makeJob(overrides: Partial<FileImportJob>): FileImportJob {
  return {
    id: 'job-1',
    projectId: 'project-1',
    fileName: 'schedule.xer',
    format: 'Xer',
    status: 'Succeeded',
    rowsImported: 42,
    errorJson: null,
    startedAt: '2026-07-27T09:00:00+07:00',
    finishedAt: '2026-07-27T09:00:02+07:00',
    createdByUserId: 'user-1',
    ...overrides,
  }
}

describe('useImportWizard', () => {
  beforeEach(() => {
    vi.mocked(api.importFile).mockReset()
    vi.mocked(api.getImportJob).mockReset()
    vi.mocked(api.getImportJobHistory).mockReset()
    vi.mocked(api.downloadProgressTemplate).mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads history on mount and exposes it as ready', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValueOnce([makeJob({})])

    const { result } = renderHook(() => useImportWizard('project-1'))

    expect(result.current.historyState).toBe('loading')
    await waitFor(() => expect(result.current.historyState).toBe('ready'))
    expect(result.current.history).toHaveLength(1)
  })

  it('surfaces a Thai error and the error state when history fails to load', async () => {
    vi.mocked(api.getImportJobHistory).mockRejectedValueOnce(
      new api.ImportApiError('โหลดประวัติการนำเข้าไม่สำเร็จ'),
    )

    const { result } = renderHook(() => useImportWizard('project-1'))

    await waitFor(() => expect(result.current.historyState).toBe('error'))
    expect(result.current.historyError).toBe('โหลดประวัติการนำเข้าไม่สำเร็จ')
  })

  it('startImport: a synchronously-terminal job goes straight to done and refreshes history', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    const succeeded = makeJob({ status: 'Succeeded', rowsImported: 7 })
    vi.mocked(api.importFile).mockResolvedValueOnce(succeeded)

    const { result } = renderHook(() => useImportWizard('project-1'))
    await waitFor(() => expect(result.current.historyState).toBe('ready'))

    const file = new File(['x'], 'schedule.xer')
    await act(async () => {
      await result.current.startImport(file, 'xer')
    })

    expect(result.current.upload.phase).toBe('done')
    expect(result.current.upload.job).toEqual(succeeded)
    expect(api.getImportJobHistory).toHaveBeenCalledTimes(2) // initial mount + post-import refresh
  })

  it('startImport: a request-level failure sets requestError, not a job', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    vi.mocked(api.importFile).mockRejectedValueOnce(
      new api.ImportApiError('ไม่พบโครงการที่ระบุ', 404),
    )

    const { result } = renderHook(() => useImportWizard('project-1'))
    await waitFor(() => expect(result.current.historyState).toBe('ready'))

    const file = new File(['x'], 'schedule.xer')
    await act(async () => {
      await result.current.startImport(file, 'xer')
    })

    expect(result.current.upload.phase).toBe('done')
    expect(result.current.upload.job).toBeNull()
    expect(result.current.upload.requestError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('startImport: a Pending job polls GET .../jobs/{id} until a terminal state is reached', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    const pending = makeJob({ status: 'Pending', rowsImported: 0 })
    const succeeded = makeJob({ status: 'Succeeded', rowsImported: 42 })
    vi.mocked(api.importFile).mockResolvedValueOnce(pending)
    vi.mocked(api.getImportJob).mockResolvedValueOnce(pending).mockResolvedValueOnce(succeeded)

    const { result } = renderHook(() => useImportWizard('project-1'))
    // Real timers for this initial settle (testing-library's `waitFor` polls via real timers) —
    // fake timers are only switched on afterward, purely to control the poll loop's own delay.
    await waitFor(() => expect(result.current.historyState).toBe('ready'))
    vi.useFakeTimers()

    const file = new File(['x'], 'schedule.xer')
    await act(async () => {
      await result.current.startImport(file, 'xer')
    })
    expect(result.current.upload.phase).toBe('polling')

    // First poll tick still comes back Pending -> stays in the polling phase.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1500)
    })
    expect(api.getImportJob).toHaveBeenCalledTimes(1)
    expect(result.current.upload.phase).toBe('polling')

    // Second poll tick reaches a terminal state -> done, and history is refreshed.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1500)
    })
    expect(api.getImportJob).toHaveBeenCalledTimes(2)
    expect(result.current.upload.phase).toBe('done')
    expect(result.current.upload.job?.status).toBe('Succeeded')
  })

  it('resetUpload returns to idle and clears the previous job/error', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    vi.mocked(api.importFile).mockResolvedValueOnce(makeJob({ status: 'Succeeded' }))

    const { result } = renderHook(() => useImportWizard('project-1'))
    await waitFor(() => expect(result.current.historyState).toBe('ready'))

    await act(async () => {
      await result.current.startImport(new File(['x'], 'schedule.xer'), 'xer')
    })
    expect(result.current.upload.phase).toBe('done')

    act(() => {
      result.current.resetUpload()
    })

    expect(result.current.upload.phase).toBe('idle')
    expect(result.current.upload.job).toBeNull()
  })

  it('setClientValidationError surfaces a Thai message through the same requestError path', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    const { result } = renderHook(() => useImportWizard('project-1'))
    await waitFor(() => expect(result.current.historyState).toBe('ready'))

    act(() => {
      result.current.setClientValidationError('นามสกุลไฟล์ไม่ตรงกับประเภทที่เลือก (.xer)')
    })

    expect(result.current.upload.phase).toBe('done')
    expect(result.current.upload.requestError).toBe('นามสกุลไฟล์ไม่ตรงกับประเภทที่เลือก (.xer)')
    expect(api.importFile).not.toHaveBeenCalled()
  })

  it('downloadTemplate: tracks loading then idle on success, error state on failure', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValue([])
    vi.mocked(api.downloadProgressTemplate).mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useImportWizard('project-1'))
    await waitFor(() => expect(result.current.historyState).toBe('ready'))

    await act(async () => {
      await result.current.downloadTemplate()
    })
    expect(result.current.templateDownload).toEqual({ state: 'idle', message: null })

    vi.mocked(api.downloadProgressTemplate).mockRejectedValueOnce(
      new api.ImportApiError('ดาวน์โหลดเทมเพลตไม่สำเร็จ'),
    )
    await act(async () => {
      await result.current.downloadTemplate()
    })
    expect(result.current.templateDownload).toEqual({
      state: 'error',
      message: 'ดาวน์โหลดเทมเพลตไม่สำเร็จ',
    })
  })
})
