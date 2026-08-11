import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWeatherLogActions } from './useWeatherLogActions'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload, WeatherLogDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, recordWeatherLog: vi.fn(), recordWeatherLogCorrection: vi.fn() }
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

const recordPayload: RecordWeatherLogPayload = {
  logDate: '2026-07-08T00:00:00.000Z',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '61.00',
  impact: 'FullStoppage',
  impactNote: null,
  hoursLost: '8.00',
  affectedActivityIds: [],
}

describe('useWeatherLogActions', () => {
  beforeEach(() => {
    vi.mocked(api.recordWeatherLog).mockReset()
    vi.mocked(api.recordWeatherLogCorrection).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  it('starts idle with no busy action or error', () => {
    const { result } = renderHook(() => useWeatherLogActions('project-1', vi.fn()))
    expect(result.current.busyAction).toBeNull()
    expect(result.current.actionError).toBeNull()
  })

  describe('record', () => {
    it('calls the API, calls onSaved, and pushes a success toast', async () => {
      vi.mocked(api.recordWeatherLog).mockResolvedValueOnce(sampleLog)
      const onSaved = vi.fn()
      const { result } = renderHook(() => useWeatherLogActions('project-1', onSaved))

      await act(async () => {
        await result.current.record(recordPayload)
      })

      expect(api.recordWeatherLog).toHaveBeenCalledWith('project-1', recordPayload)
      expect(onSaved).toHaveBeenCalledTimes(1)
      expect(result.current.busyAction).toBeNull()
      expect(useToastStore.getState().toasts).toHaveLength(1)
    })

    it('sets actionError and does NOT call onSaved on failure', async () => {
      vi.mocked(api.recordWeatherLog).mockRejectedValueOnce(new api.WeatherApiError('บันทึกสภาพอากาศไม่สำเร็จ'))
      const onSaved = vi.fn()
      const { result } = renderHook(() => useWeatherLogActions('project-1', onSaved))

      await act(async () => {
        await result.current.record(recordPayload)
      })

      expect(result.current.actionError).toBe('บันทึกสภาพอากาศไม่สำเร็จ')
      expect(onSaved).not.toHaveBeenCalled()
    })
  })

  describe('correct', () => {
    const correctionPayload: RecordWeatherLogCorrectionPayload = {
      entryKind: 'Correction',
      correctionReason: 'พิมพ์ผิด',
      logDate: '2026-07-08T00:00:00.000Z',
      condition: 'HeavyRain',
      conditionNote: null,
      rainfallMm: '61.00',
      impact: 'FullStoppage',
      impactNote: null,
      hoursLost: '3.00',
      affectedActivityIds: [],
    }

    it('calls the corrections endpoint with the target log id and calls onSaved', async () => {
      vi.mocked(api.recordWeatherLogCorrection).mockResolvedValueOnce({ ...sampleLog, id: 'log-2', entryKind: 'Correction' })
      const onSaved = vi.fn()
      const { result } = renderHook(() => useWeatherLogActions('project-1', onSaved))

      await act(async () => {
        await result.current.correct('log-1', correctionPayload)
      })

      expect(api.recordWeatherLogCorrection).toHaveBeenCalledWith('project-1', 'log-1', correctionPayload)
      expect(onSaved).toHaveBeenCalledTimes(1)
    })

    it('a Retraction pushes a distinct "voided" toast message', async () => {
      vi.mocked(api.recordWeatherLogCorrection).mockResolvedValueOnce({ ...sampleLog, id: 'log-2', entryKind: 'Retraction' })
      const { result } = renderHook(() => useWeatherLogActions('project-1', vi.fn()))

      await act(async () => {
        await result.current.correct('log-1', { ...correctionPayload, entryKind: 'Retraction' })
      })

      expect(useToastStore.getState().toasts[0]?.message).toContain('เพิกถอน')
    })

    it('sets actionError on failure (e.g. stale chain-tail target)', async () => {
      vi.mocked(api.recordWeatherLogCorrection).mockRejectedValueOnce(
        new api.WeatherApiError('บันทึกนี้มีรายการแก้ไขอื่นอยู่แล้ว กรุณาโหลดข้อมูลใหม่แล้วแก้ไขจากรายการล่าสุดของสายการแก้ไขนี้แทน'),
      )
      const { result } = renderHook(() => useWeatherLogActions('project-1', vi.fn()))

      await act(async () => {
        await result.current.correct('log-1', correctionPayload)
      })

      expect(result.current.actionError).toContain('โหลดข้อมูลใหม่')
    })
  })

  it('clearActionError resets actionError to null', async () => {
    vi.mocked(api.recordWeatherLog).mockRejectedValueOnce(new api.WeatherApiError('failed'))
    const { result } = renderHook(() => useWeatherLogActions('project-1', vi.fn()))
    await act(async () => {
      await result.current.record(recordPayload)
    })
    expect(result.current.actionError).not.toBeNull()

    act(() => result.current.clearActionError())
    expect(result.current.actionError).toBeNull()
  })
})
