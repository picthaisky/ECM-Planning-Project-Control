import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useUpdateApprovalPolicy } from './useUpdateApprovalPolicy'
import * as api from './api'
import type { ApprovalPolicy, UpdateApprovalPolicyPayload } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, updateApprovalPolicy: vi.fn() }
})

const payload: UpdateApprovalPolicyPayload = {
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: [{ stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 }],
}

const saved: ApprovalPolicy = {
  documentType: 'PaymentCertificate',
  version: 5,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: payload.rules,
}

describe('useUpdateApprovalPolicy', () => {
  beforeEach(() => {
    vi.mocked(api.updateApprovalPolicy).mockReset()
  })

  it('starts idle with no saved version', () => {
    const { result } = renderHook(() => useUpdateApprovalPolicy('tenant-1', 'PaymentCertificate'))
    expect(result.current.saveState).toBe('idle')
    expect(result.current.savedVersion).toBeNull()
  })

  it('surfaces the new version number from the real PUT response on success (S9-FE-03 DoD)', async () => {
    vi.mocked(api.updateApprovalPolicy).mockResolvedValueOnce(saved)
    const { result } = renderHook(() => useUpdateApprovalPolicy('tenant-1', 'PaymentCertificate'))

    await act(async () => {
      await result.current.save(payload)
    })

    expect(api.updateApprovalPolicy).toHaveBeenCalledWith('tenant-1', 'PaymentCertificate', payload)
    expect(result.current.savedVersion).toBe(5)
    expect(result.current.saveState).toBe('idle')
    expect(result.current.saveError).toBeNull()
  })

  it('a band-overlap failure sets both saveError and the distinct bandError with the offending step', async () => {
    vi.mocked(api.updateApprovalPolicy).mockRejectedValueOnce(
      new api.ApprovalPolicyBandError('พบช่วงจำนวนเงินที่ทับซ้อนกันในขั้นตอนที่ 2', 'BandOverlap', 2, 400),
    )
    const { result } = renderHook(() => useUpdateApprovalPolicy('tenant-1', 'PaymentCertificate'))

    await act(async () => {
      await result.current.save(payload)
    })

    expect(result.current.saveState).toBe('error')
    expect(result.current.saveError).toContain('ขั้นตอนที่ 2')
    expect(result.current.bandError).toMatchObject({ problem: 'BandOverlap', invalidStepNo: 2 })
    expect(result.current.savedVersion).toBeNull()
  })

  it('a generic failure sets saveError but leaves bandError null', async () => {
    vi.mocked(api.updateApprovalPolicy).mockRejectedValueOnce(new api.TenantAdminApiError('ผิดพลาด', 500))
    const { result } = renderHook(() => useUpdateApprovalPolicy('tenant-1', 'PaymentCertificate'))

    await act(async () => {
      await result.current.save(payload)
    })

    expect(result.current.saveError).toBe('ผิดพลาด')
    expect(result.current.bandError).toBeNull()
  })

  it('clearSavedVersion resets the "just saved" banner', async () => {
    vi.mocked(api.updateApprovalPolicy).mockResolvedValueOnce(saved)
    const { result } = renderHook(() => useUpdateApprovalPolicy('tenant-1', 'PaymentCertificate'))

    await act(async () => {
      await result.current.save(payload)
    })
    expect(result.current.savedVersion).toBe(5)

    act(() => result.current.clearSavedVersion())
    expect(result.current.savedVersion).toBeNull()
  })
})
