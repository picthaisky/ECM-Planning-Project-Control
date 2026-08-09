import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useApprovalActions } from './useApprovalActions'
import * as api from './api'
import type { ApprovalActionDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getApprovalActions: vi.fn() }
})

describe('useApprovalActions', () => {
  beforeEach(() => {
    vi.mocked(api.getApprovalActions).mockReset()
  })

  it('ready state: resolves with the history when the endpoint answers', async () => {
    const history: ApprovalActionDto[] = [
      {
        id: 'action-1',
        documentType: 'PaymentCertificate',
        documentId: 'cert-1',
        revisionNo: 1,
        stepNo: 1,
        actorUserId: 'user-1',
        actorRoleAtTime: 'QS',
        action: 'Approve',
        comment: 'ตรวจแล้ว',
        actedAt: '2026-08-02T10:00:00+07:00',
        approvalPolicyId: 'policy-1',
        approvalPolicyVersion: 2,
      },
    ]
    vi.mocked(api.getApprovalActions).mockResolvedValueOnce(history)

    const { result } = renderHook(() => useApprovalActions('cert-1'))
    expect(result.current.state).toBe('loading')

    await waitFor(() => expect(result.current.state).toBe('ready'))
    expect(result.current.actions).toEqual(history)
    expect(result.current.unavailableReason).toBeNull()
  })

  it('unavailable state: a 404 (the endpoint is not implemented on the real backend yet) degrades gracefully, not as a scary error', async () => {
    vi.mocked(api.getApprovalActions).mockRejectedValueOnce(new api.PaymentApiError('ไม่พบข้อมูลที่ระบุ', 404))

    const { result } = renderHook(() => useApprovalActions('cert-1'))

    await waitFor(() => expect(result.current.state).toBe('unavailable'))
    expect(result.current.actions).toBeNull()
    expect(result.current.unavailableReason).toBe('ไม่พบข้อมูลที่ระบุ')
  })

  it('unavailable immediately, with no request, when there is no certificate id yet', () => {
    const { result } = renderHook(() => useApprovalActions(null))
    expect(result.current.state).toBe('unavailable')
    expect(api.getApprovalActions).not.toHaveBeenCalled()
  })
})
