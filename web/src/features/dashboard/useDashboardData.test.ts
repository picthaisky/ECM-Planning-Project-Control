import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useDashboardData } from './useDashboardData'
import * as api from './api'
import type { DashboardResponseDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getDashboard: vi.fn() }
})

const sampleDashboard: DashboardResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pv: '285180000.00',
  ev: '262970000.00',
  ac: '253100000.00',
  sv: '-22210000.00',
  cv: '9870000.00',
  spi: '0.920000',
  cpi: '1.038995',
  actualCostEntryCount: 42,
  eacVariant: 'CpiBased',
  performanceFactor: '0.962477',
  etc: '213900000.00',
  eac: '466000000.00',
  vac: '19000000.00',
  eacComputable: true,
  eacNullReason: null,
  progressRollup: { progressPercentage: '54.20', weightWarnings: [], mixedScopeWbsNodeIds: [] },
  warnings: [],
  cumulativeDisbursement: '0.00',
  cumulativeWeatherStoppageDays: 0,
}

describe('useDashboardData', () => {
  beforeEach(() => {
    vi.mocked(api.getDashboard).mockReset()
  })

  it('starts loading, then resolves to ready with the fetched DashboardResponseDto', async () => {
    vi.mocked(api.getDashboard).mockResolvedValueOnce(sampleDashboard)

    const { result } = renderHook(() => useDashboardData('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.dashboard).toEqual(sampleDashboard)
    expect(result.current.loadError).toBeNull()
    expect(api.getDashboard).toHaveBeenCalledWith('project-1')
  })

  it('goes to error with the Thai DashboardApiError message on failure', async () => {
    vi.mocked(api.getDashboard).mockRejectedValueOnce(new api.DashboardApiError('ไม่พบโครงการที่ระบุ', 404))

    const { result } = renderHook(() => useDashboardData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.dashboard).toBeNull()
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('falls back to a generic Thai message for a non-DashboardApiError rejection', async () => {
    vi.mocked(api.getDashboard).mockRejectedValueOnce(new Error('boom'))

    const { result } = renderHook(() => useDashboardData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดข้อมูล Dashboard ไม่สำเร็จ')
  })

  it('reload() re-fetches on demand', async () => {
    vi.mocked(api.getDashboard).mockResolvedValueOnce(sampleDashboard)
    const { result } = renderHook(() => useDashboardData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const updated = { ...sampleDashboard, ac: '260000000.00' }
    vi.mocked(api.getDashboard).mockResolvedValueOnce(updated)

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.dashboard).toEqual(updated)
    expect(api.getDashboard).toHaveBeenCalledTimes(2)
  })
})
