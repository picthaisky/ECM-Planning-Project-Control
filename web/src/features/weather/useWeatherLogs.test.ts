import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWeatherLogs } from './useWeatherLogs'
import * as api from './api'
import type { WeatherLogDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, listWeatherLogs: vi.fn() }
})

const sampleLog: WeatherLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  logDate: '2026-07-08T00:00:00+07:00',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '61.00',
  impact: 'FullStoppage',
  impactNote: null,
  hoursLost: '8.00',
  workStoppage: true,
  entryKind: 'Original',
  correctsWeatherLogId: null,
  correctionReason: null,
  affectedActivityIds: [],
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-08T09:00:00+07:00',
}

describe('useWeatherLogs', () => {
  beforeEach(() => {
    vi.mocked(api.listWeatherLogs).mockReset()
  })

  it('loads on mount', async () => {
    vi.mocked(api.listWeatherLogs).mockResolvedValueOnce([sampleLog])

    const { result } = renderHook(() => useWeatherLogs('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.logs).toEqual([sampleLog])
    expect(result.current.loadError).toBeNull()
  })

  it('surfaces a Thai error message on failure', async () => {
    vi.mocked(api.listWeatherLogs).mockRejectedValueOnce(new api.WeatherApiError('โหลดบันทึกสภาพอากาศไม่สำเร็จ'))

    const { result } = renderHook(() => useWeatherLogs('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('โหลดบันทึกสภาพอากาศไม่สำเร็จ')
    expect(result.current.logs).toEqual([])
  })

  it('reload() re-fetches and replaces the list', async () => {
    vi.mocked(api.listWeatherLogs).mockResolvedValueOnce([sampleLog])
    const { result } = renderHook(() => useWeatherLogs('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const secondLog = { ...sampleLog, id: 'log-2' }
    vi.mocked(api.listWeatherLogs).mockResolvedValueOnce([sampleLog, secondLog])
    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.logs).toHaveLength(2)
  })
})
