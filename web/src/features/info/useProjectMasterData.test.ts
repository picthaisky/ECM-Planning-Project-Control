import { renderHook, waitFor } from '@testing-library/react'
import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useProjectMasterData } from './useProjectMasterData'
import * as api from './api'
import type { Project } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getProject: vi.fn(), updateProject: vi.fn() }
})

const sampleProject: Project = {
  id: 'project-1',
  name: 'Riverside Condominium Tower B',
  code: 'RCT-B',
  owner: 'Siam Riverside Development PLC',
  contractStart: '2025-10-01T00:00:00+07:00',
  contractFinish: '2027-03-31T00:00:00+07:00',
  bac: '850000000.00',
  contractValue: '850000000.00',
  retentionRate: '5.00',
  advanceRate: '10.00',
  retentionCapPercentage: null,
  retentionRelease1Percentage: '50.00',
  defectsLiabilityMonths: 24,
  advanceAmountPaid: null,
  advanceRecoveryMethod: 'ProRata',
  advanceRecoveryStartPct: null,
  advanceRecoveryRatePct: null,
  advanceRecoveryEndPct: null,
}

describe('useProjectMasterData', () => {
  beforeEach(() => {
    vi.mocked(api.getProject).mockReset()
    vi.mocked(api.updateProject).mockReset()
  })

  it('loads the project on mount and exposes it as ready', async () => {
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)

    const { result } = renderHook(() => useProjectMasterData('project-1'))

    expect(result.current.loadState).toBe('loading')
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.project).toEqual(sampleProject)
  })

  it('surfaces a Thai error and the error state when the load fails', async () => {
    vi.mocked(api.getProject).mockRejectedValueOnce(
      new api.ProjectApiError('ไม่พบโครงการที่ระบุ', 404),
    )

    const { result } = renderHook(() => useProjectMasterData('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  it('startEdit/cancelEdit toggle isEditing without touching the loaded project', async () => {
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    const { result } = renderHook(() => useProjectMasterData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.startEdit())
    expect(result.current.isEditing).toBe(true)

    act(() => result.current.cancelEdit())
    expect(result.current.isEditing).toBe(false)
    expect(result.current.project).toEqual(sampleProject)
  })

  it('save() calls updateProject, replaces the project with the response, and exits edit mode', async () => {
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    const updated = { ...sampleProject, retentionRate: '7.50' }
    vi.mocked(api.updateProject).mockResolvedValueOnce(updated)

    const { result } = renderHook(() => useProjectMasterData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    act(() => result.current.startEdit())

    let saveResult: boolean | undefined
    await act(async () => {
      saveResult = await result.current.save({
        name: sampleProject.name,
        code: sampleProject.code,
        owner: sampleProject.owner,
        contractStart: sampleProject.contractStart,
        contractFinish: sampleProject.contractFinish,
        bac: sampleProject.bac,
        contractValue: sampleProject.contractValue,
        retentionRate: '7.50',
        advanceRate: sampleProject.advanceRate,
        retentionCapPercentage: null,
        retentionRelease1Percentage: sampleProject.retentionRelease1Percentage,
        defectsLiabilityMonths: sampleProject.defectsLiabilityMonths,
        advanceAmountPaid: null,
        advanceRecoveryMethod: 'ProRata',
        advanceRecoveryStartPct: null,
        advanceRecoveryRatePct: null,
        advanceRecoveryEndPct: null,
      })
    })

    expect(saveResult).toBe(true)
    expect(result.current.project).toEqual(updated)
    expect(result.current.isEditing).toBe(false)
    expect(result.current.saveState).toBe('idle')
  })

  it('save() keeps edit mode open and surfaces the Thai error on a server rejection', async () => {
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    vi.mocked(api.updateProject).mockRejectedValueOnce(
      new api.ProjectApiError('ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง', 400),
    )

    const { result } = renderHook(() => useProjectMasterData('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    act(() => result.current.startEdit())

    let saveResult: boolean | undefined
    await act(async () => {
      saveResult = await result.current.save({
        name: sampleProject.name,
        code: sampleProject.code,
        owner: sampleProject.owner,
        contractStart: sampleProject.contractStart,
        contractFinish: sampleProject.contractFinish,
        bac: sampleProject.bac,
        contractValue: sampleProject.contractValue,
        retentionRate: sampleProject.retentionRate,
        advanceRate: sampleProject.advanceRate,
        retentionCapPercentage: null,
        retentionRelease1Percentage: sampleProject.retentionRelease1Percentage,
        defectsLiabilityMonths: sampleProject.defectsLiabilityMonths,
        advanceAmountPaid: null,
        advanceRecoveryMethod: 'ProRata',
        advanceRecoveryStartPct: null,
        advanceRecoveryRatePct: null,
        advanceRecoveryEndPct: null,
      })
    })

    expect(saveResult).toBe(false)
    expect(result.current.isEditing).toBe(true)
    expect(result.current.saveError).toBe('ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง')
  })
})
