import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSetEacVariantDefault } from './useSetEacVariantDefault'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { SetEacVariantDefaultResult } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, setEacVariantDefault: vi.fn() }
})

describe('useSetEacVariantDefault', () => {
  beforeEach(() => {
    vi.mocked(api.setEacVariantDefault).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  it('starts idle with no error', () => {
    const { result } = renderHook(() => useSetEacVariantDefault('project-1'))
    expect(result.current.state).toBe('idle')
    expect(result.current.error).toBeNull()
  })

  it('saving -> idle on success, returns the persisted result, and raises a naming toast', async () => {
    const resultDto: SetEacVariantDefaultResult = { projectId: 'project-1', eacVariantDefault: 'CpiSpiBased' }
    let resolvePromise!: (value: SetEacVariantDefaultResult) => void
    vi.mocked(api.setEacVariantDefault).mockReturnValueOnce(
      new Promise((resolve) => {
        resolvePromise = resolve
      }),
    )

    const { result } = renderHook(() => useSetEacVariantDefault('project-1'))

    let savePromise!: Promise<SetEacVariantDefaultResult | null>
    act(() => {
      savePromise = result.current.save('CpiSpiBased')
    })
    expect(result.current.state).toBe('saving')

    await act(async () => {
      resolvePromise(resultDto)
      await savePromise
    })

    expect(result.current.state).toBe('idle')
    expect(await savePromise).toEqual(resultDto)
    expect(api.setEacVariantDefault).toHaveBeenCalledWith('project-1', 'CpiSpiBased')

    await waitFor(() => expect(useToastStore.getState().toasts).toHaveLength(1))
    expect(useToastStore.getState().toasts[0].message).toContain('CPI × SPI')
  })

  it('saving -> error surfaces the Thai EvmApiError message, returns null, and pushes no toast', async () => {
    vi.mocked(api.setEacVariantDefault).mockRejectedValueOnce(
      new api.EvmApiError('คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้', 403),
    )

    const { result } = renderHook(() => useSetEacVariantDefault('project-1'))

    let saveResult: SetEacVariantDefaultResult | null = null
    await act(async () => {
      saveResult = await result.current.save('Atypical')
    })

    expect(result.current.state).toBe('error')
    expect(result.current.error).toBe('คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้')
    expect(saveResult).toBeNull()
    expect(useToastStore.getState().toasts).toHaveLength(0)
  })

  it('falls back to a generic Thai message for a non-EvmApiError rejection', async () => {
    vi.mocked(api.setEacVariantDefault).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useSetEacVariantDefault('project-1'))

    await act(async () => {
      await result.current.save('CpiBased')
    })

    expect(result.current.error).toBe('ตั้งค่าเริ่มต้นของโครงการไม่สำเร็จ')
  })
})
