import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useGanttData } from './useGanttData'
import * as api from './api'
import type { GanttDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getGantt: vi.fn() }
})

const sampleGantt: GanttDto = {
  projectId: 'project-1',
  dataDate: '2026-03-15T00:00:00+07:00',
  activities: [
    {
      id: 'a1',
      wbsNodeId: 'node-1',
      activityCode: 'ACT-101',
      name: 'งานเสาเข็มและฐานราก',
      plannedStart: '2026-01-01T00:00:00+07:00',
      plannedFinish: '2026-01-16T00:00:00+07:00',
      actualStart: null,
      actualFinish: null,
      isCritical: true,
      totalFloat: 0,
      freeFloat: 0,
    },
  ],
}

describe('useGanttData', () => {
  beforeEach(() => {
    vi.mocked(api.getGantt).mockReset()
  })

  it('loads activities on mount', async () => {
    vi.mocked(api.getGantt).mockResolvedValueOnce(sampleGantt)
    const { result } = renderHook(() => useGanttData('project-1'))

    expect(result.current.loadState).toBe('loading')
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.activities).toEqual(sampleGantt.activities)
    expect(result.current.dataDateIso).toBe(sampleGantt.dataDate)
  })

  it('surfaces a Thai error state on load failure', async () => {
    vi.mocked(api.getGantt).mockRejectedValueOnce(new api.GanttApiError('ไม่พบโครงการที่ระบุ', 404))
    const { result } = renderHook(() => useGanttData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('reload() re-fetches and can recover from a prior error', async () => {
    vi.mocked(api.getGantt).mockRejectedValueOnce(new api.GanttApiError('โหลดข้อมูล Gantt ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'))
    const { result } = renderHook(() => useGanttData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('error'))

    vi.mocked(api.getGantt).mockResolvedValueOnce(sampleGantt)
    await result.current.reload()

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.activities).toEqual(sampleGantt.activities)
  })
})
