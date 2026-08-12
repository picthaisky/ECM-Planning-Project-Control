import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useRoutingSimulator } from './useRoutingSimulator'
import * as api from './api'
import type { ApprovalRoutingSimulation, SimulateApprovalRoutingPayload } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, simulateApprovalRouting: vi.fn() }
})

const payload: SimulateApprovalRoutingPayload = { projectId: 'project-1', amount: '2400000.00' }

const simulation: ApprovalRoutingSimulation = {
  documentType: 'VariationOrder',
  projectId: 'project-1',
  inputAmount: '2400000.00',
  routingAmount: '2400000.00',
  approvalPolicyId: 'policy-2',
  approvalPolicyVersion: 2,
  usedFallbackChain: false,
  steps: [{ stepNo: 1, requiredRole: 'PM', quorumCount: 1 }],
  escalationApplied: false,
  allowSelfApproval: false,
  multipleActivePoliciesDetected: false,
  ambiguousActivePolicies: [],
}

describe('useRoutingSimulator', () => {
  beforeEach(() => {
    vi.mocked(api.simulateApprovalRouting).mockReset()
  })

  it('starts idle with no result', () => {
    const { result } = renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))
    expect(result.current.state).toBe('idle')
    expect(result.current.result).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('does not call the API on mount — it is a trigger the Admin explicitly runs, not an auto-load', () => {
    renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))
    expect(api.simulateApprovalRouting).not.toHaveBeenCalled()
  })

  it('surfaces the resolved chain on success', async () => {
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce(simulation)
    const { result } = renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))

    await act(async () => {
      await result.current.simulate(payload)
    })

    expect(api.simulateApprovalRouting).toHaveBeenCalledWith('tenant-1', 'VariationOrder', payload)
    expect(result.current.result).toEqual(simulation)
    expect(result.current.state).toBe('idle')
    expect(result.current.error).toBeNull()
  })

  it('a failure sets the error state and clears any previous result', async () => {
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce(simulation)
    const { result } = renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))
    await act(async () => {
      await result.current.simulate(payload)
    })
    expect(result.current.result).toEqual(simulation)

    vi.mocked(api.simulateApprovalRouting).mockRejectedValueOnce(
      new api.ApprovalRoutingSimulationApiError('ไม่พบโครงการนี้ในองค์กรของคุณ', 'ProjectNotFound', 404),
    )
    await act(async () => {
      await result.current.simulate({ ...payload, projectId: 'missing-project' })
    })

    expect(result.current.state).toBe('error')
    expect(result.current.error).toBe('ไม่พบโครงการนี้ในองค์กรของคุณ')
    expect(result.current.result).toBeNull()
  })

  it('a generic (non-ApprovalRoutingSimulationApiError) failure still gets a Thai fallback message', async () => {
    vi.mocked(api.simulateApprovalRouting).mockRejectedValueOnce(new Error('boom'))
    const { result } = renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))

    await act(async () => {
      await result.current.simulate(payload)
    })

    expect(result.current.state).toBe('error')
    expect(result.current.error).toBe('จำลองเส้นทางอนุมัติไม่สำเร็จ')
  })

  it('reset() clears result/error/state back to idle', async () => {
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce(simulation)
    const { result } = renderHook(() => useRoutingSimulator('tenant-1', 'VariationOrder'))
    await act(async () => {
      await result.current.simulate(payload)
    })
    expect(result.current.result).toEqual(simulation)

    act(() => result.current.reset())

    expect(result.current.result).toBeNull()
    expect(result.current.error).toBeNull()
    expect(result.current.state).toBe('idle')
  })
})
