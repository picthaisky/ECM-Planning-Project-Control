import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useApprovalPolicy } from './useApprovalPolicy'
import * as api from './api'
import type { ApprovalPolicy } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getApprovalPolicy: vi.fn() }
})

const policy: ApprovalPolicy = {
  documentType: 'PaymentCertificate',
  version: 2,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: [{ stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 }],
}

describe('useApprovalPolicy', () => {
  beforeEach(() => {
    vi.mocked(api.getApprovalPolicy).mockReset()
  })

  it('ready: resolves with the active policy', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValueOnce(policy)

    const { result } = renderHook(() => useApprovalPolicy('tenant-1', 'PaymentCertificate'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.policy).toEqual(policy)
  })

  it('not-configured: a 404 (ApprovalPolicyNotFound) is a distinct, actionable state — not a hard error', async () => {
    vi.mocked(api.getApprovalPolicy).mockRejectedValueOnce(
      new api.TenantAdminApiError('ยังไม่มีการตั้งค่านโยบายอนุมัติสำหรับประเภทเอกสารนี้', 404),
    )

    const { result } = renderHook(() => useApprovalPolicy('tenant-1', 'VariationOrder'))

    await waitFor(() => expect(result.current.loadState).toBe('not-configured'))
    expect(result.current.policy).toBeNull()
    expect(result.current.loadError).toBeNull()
  })

  it('error: any non-404 failure is a genuine error state with a Thai message', async () => {
    vi.mocked(api.getApprovalPolicy).mockRejectedValueOnce(new api.TenantAdminApiError('เกิดข้อผิดพลาด', 500))

    const { result } = renderHook(() => useApprovalPolicy('tenant-1', 'PaymentCertificate'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('เกิดข้อผิดพลาด')
  })

  it('reload() re-fetches', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(policy)
    const { result } = renderHook(() => useApprovalPolicy('tenant-1', 'PaymentCertificate'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    await act(async () => {
      await result.current.reload()
    })
    expect(api.getApprovalPolicy).toHaveBeenCalledTimes(2)
  })
})
