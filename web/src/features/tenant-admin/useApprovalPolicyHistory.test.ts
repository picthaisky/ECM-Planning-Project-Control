import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useApprovalPolicyHistory } from './useApprovalPolicyHistory'
import * as api from './api'
import type { ApprovalPolicyVersionHistoryEntry } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getApprovalPolicyHistory: vi.fn() }
})

const entries: ApprovalPolicyVersionHistoryEntry[] = [
  {
    approvalPolicyId: 'policy-1',
    version: 1,
    isActive: false,
    effectiveFrom: '2026-01-01T00:00:00+07:00',
    effectiveTo: '2026-06-01T00:00:00+07:00',
    allowSelfApproval: true,
    cumulativeVoEscalationPct: null,
    cumulativeVoEscalationRole: null,
    ruleCount: 1,
    createdByUserId: 'user-1',
    createdAt: '2026-01-01T00:00:00+07:00',
    lastModifiedByUserId: null,
    lastModifiedAt: null,
  },
  {
    approvalPolicyId: 'policy-2',
    version: 2,
    isActive: true,
    effectiveFrom: '2026-06-01T00:00:00+07:00',
    effectiveTo: null,
    allowSelfApproval: false,
    cumulativeVoEscalationPct: null,
    cumulativeVoEscalationRole: null,
    ruleCount: 2,
    createdByUserId: 'user-1',
    createdAt: '2026-06-01T00:00:00+07:00',
    lastModifiedByUserId: 'user-1',
    lastModifiedAt: '2026-06-01T00:00:00+07:00',
  },
]

describe('useApprovalPolicyHistory', () => {
  beforeEach(() => {
    vi.mocked(api.getApprovalPolicyHistory).mockReset()
  })

  it('ready: resolves with the full version list', async () => {
    vi.mocked(api.getApprovalPolicyHistory).mockResolvedValueOnce(entries)

    const { result } = renderHook(() => useApprovalPolicyHistory('tenant-1', 'VariationOrder'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.entries).toEqual(entries)
    expect(result.current.loadError).toBeNull()
  })

  it('ready: an empty history is a legitimate ready state, not an error (no "not configured" 404 case here)', async () => {
    vi.mocked(api.getApprovalPolicyHistory).mockResolvedValueOnce([])

    const { result } = renderHook(() => useApprovalPolicyHistory('tenant-1', 'PaymentCertificate'))

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.entries).toEqual([])
  })

  it('error: a genuine failure sets a Thai error message', async () => {
    vi.mocked(api.getApprovalPolicyHistory).mockRejectedValueOnce(new api.TenantAdminApiError('เกิดข้อผิดพลาด', 500))

    const { result } = renderHook(() => useApprovalPolicyHistory('tenant-1', 'PaymentCertificate'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('เกิดข้อผิดพลาด')
    expect(result.current.entries).toEqual([])
  })

  it('reload() re-fetches', async () => {
    vi.mocked(api.getApprovalPolicyHistory).mockResolvedValue(entries)
    const { result } = renderHook(() => useApprovalPolicyHistory('tenant-1', 'VariationOrder'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    await act(async () => {
      await result.current.reload()
    })
    expect(api.getApprovalPolicyHistory).toHaveBeenCalledTimes(2)
  })
})
