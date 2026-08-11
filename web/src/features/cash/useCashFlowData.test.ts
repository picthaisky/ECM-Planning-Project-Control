import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useCashFlowData } from './useCashFlowData'
import * as api from './api'
import type { CashFlowResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getCashFlow: vi.fn() }
})

const sampleCashFlow: CashFlowResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pvCumulative: '285180000.00',
  evCumulative: '262970000.00',
  acCumulative: '253100000.00',
  actualCostEntryCount: 42,
  periods: [],
  receipts: { isAvailable: false, cumulative: null, periods: [], unavailableReason: 'PaymentCertificatesNotYetImplemented' },
  netCashPosition: null,
  warnings: [],
}

describe('useCashFlowData', () => {
  beforeEach(() => {
    vi.mocked(api.getCashFlow).mockReset()
  })

  it('starts loading, then resolves to ready with the fetched CashFlowResponseDto', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce(sampleCashFlow)

    const { result } = renderHook(() => useCashFlowData('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.cashFlow).toEqual(sampleCashFlow)
    expect(result.current.loadError).toBeNull()
    expect(api.getCashFlow).toHaveBeenCalledWith('project-1')
  })

  it('goes to error with the Thai CashFlowApiError message on failure', async () => {
    vi.mocked(api.getCashFlow).mockRejectedValueOnce(new api.CashFlowApiError('ไม่พบโครงการที่ระบุ', 404))

    const { result } = renderHook(() => useCashFlowData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.cashFlow).toBeNull()
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('falls back to a generic Thai message for a non-CashFlowApiError rejection', async () => {
    vi.mocked(api.getCashFlow).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useCashFlowData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดข้อมูล Cash Flow ไม่สำเร็จ')
  })

  it('reload() re-fetches on demand', async () => {
    vi.mocked(api.getCashFlow).mockResolvedValueOnce(sampleCashFlow)
    const { result } = renderHook(() => useCashFlowData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const updated = { ...sampleCashFlow, acCumulative: '260000000.00' }
    vi.mocked(api.getCashFlow).mockResolvedValueOnce(updated)

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.cashFlow).toEqual(updated)
    expect(api.getCashFlow).toHaveBeenCalledTimes(2)
  })
})
