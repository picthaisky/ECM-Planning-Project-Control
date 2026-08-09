import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEvmData } from './useEvmData'
import * as api from './api'
import type { EvmResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getEvm: vi.fn() }
})

const sampleEvm: EvmResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-06-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '400000.00',
  ev: '300000.00',
  ac: '350000.00',
  sv: '-100000.00',
  cv: '-50000.00',
  spi: '0.750000',
  cpi: '0.857143',
  tcpiBac: '1.076923',
  tcpiEac: '0.857143',
  selectedVariant: 'CpiBased',
  variants: [],
  warnings: [],
}

describe('useEvmData', () => {
  beforeEach(() => {
    vi.mocked(api.getEvm).mockReset()
  })

  it('starts loading, then resolves to ready with the fetched EvmResponseDto', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(sampleEvm)

    const { result } = renderHook(() => useEvmData('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.evm).toEqual(sampleEvm)
    expect(result.current.loadError).toBeNull()
    expect(api.getEvm).toHaveBeenCalledWith('project-1')
  })

  it('goes to error with the Thai EvmApiError message on failure', async () => {
    vi.mocked(api.getEvm).mockRejectedValueOnce(new api.EvmApiError('ไม่พบโครงการที่ระบุ', 404))

    const { result } = renderHook(() => useEvmData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.evm).toBeNull()
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('falls back to a generic Thai message for a non-EvmApiError rejection', async () => {
    vi.mocked(api.getEvm).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useEvmData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดข้อมูล EVM ไม่สำเร็จ')
  })

  it('reload() re-fetches on demand', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(sampleEvm)
    const { result } = renderHook(() => useEvmData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const updated = { ...sampleEvm, selectedVariant: 'CpiSpiBased' as const }
    vi.mocked(api.getEvm).mockResolvedValueOnce(updated)

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.evm).toEqual(updated)
    expect(api.getEvm).toHaveBeenCalledTimes(2)
  })
})
