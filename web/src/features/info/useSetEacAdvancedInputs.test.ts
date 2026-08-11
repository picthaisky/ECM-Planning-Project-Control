import { renderHook, waitFor } from '@testing-library/react'
import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSetEacAdvancedInputs } from './useSetEacAdvancedInputs'
import * as api from './api'
import type { SetEacAdvancedInputsResult } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, setEacAdvancedInputs: vi.fn() }
})

describe('useSetEacAdvancedInputs', () => {
  beforeEach(() => {
    vi.mocked(api.setEacAdvancedInputs).mockReset()
  })

  it('calls setEacAdvancedInputs and forwards the fresh EAC config to onSaved', async () => {
    const result: SetEacAdvancedInputsResult = {
      projectId: 'project-1',
      eacManualEtc: '760000.00',
      eacCustomPerformanceFactor: '1.2000',
      eacManualEtcStaleSince: null,
    }
    vi.mocked(api.setEacAdvancedInputs).mockResolvedValueOnce(result)
    const onSaved = vi.fn()

    const { result: hook } = renderHook(() => useSetEacAdvancedInputs('project-1'))

    await act(async () => {
      await hook.current.save({ eacManualEtc: '760000.00', eacCustomPerformanceFactor: '1.2000' }, onSaved)
    })

    expect(api.setEacAdvancedInputs).toHaveBeenCalledWith('project-1', {
      eacManualEtc: '760000.00',
      eacCustomPerformanceFactor: '1.2000',
    })
    expect(onSaved).toHaveBeenCalledWith({
      eacManualEtc: '760000.00',
      eacCustomPerformanceFactor: '1.2000',
      eacManualEtcStaleSince: null,
    })
    await waitFor(() => expect(hook.current.state).toBe('idle'))
  })

  it('on failure, sets the error state and never calls onSaved', async () => {
    vi.mocked(api.setEacAdvancedInputs).mockRejectedValueOnce(
      new api.ProjectApiError('บันทึกไม่สำเร็จ', 400),
    )
    const onSaved = vi.fn()

    const { result: hook } = renderHook(() => useSetEacAdvancedInputs('project-1'))

    await act(async () => {
      await hook.current.save({ eacManualEtc: null, eacCustomPerformanceFactor: null }, onSaved)
    })

    expect(hook.current.state).toBe('error')
    expect(hook.current.error).toBe('บันทึกไม่สำเร็จ')
    expect(onSaved).not.toHaveBeenCalled()
  })
})
