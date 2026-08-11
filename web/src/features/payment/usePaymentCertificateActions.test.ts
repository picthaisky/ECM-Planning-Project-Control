import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePaymentCertificateActions } from './usePaymentCertificateActions'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { PaymentCertificateDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    submitPaymentCertificate: vi.fn(),
    approvePaymentCertificate: vi.fn(),
    returnPaymentCertificateForRevision: vi.fn(),
    rejectPaymentCertificate: vi.fn(),
    recordPayment: vi.fn(),
  }
})

const baseCertificate: PaymentCertificateDto = {
  id: 'cert-1',
  projectId: 'project-1',
  milestoneNo: 3,
  description: 'งานโครงสร้างชั้น 3-5',
  milestoneValue: '21600000.00',
  previousCumulativeApprovePct: '0.00',
  approvePct: '100.00',
  claimPct: '100.00',
  actualProgressPct: '100.00',
  grossCertifiedAmount: '21600000.00',
  retentionAmount: '1080000.00',
  advanceRecoveryAmount: '2160000.00',
  netPayment: '18360000.00',
  status: 'PendingApproval',
  revisionNo: 1,
  currentStepNo: 1,
  totalSteps: 2,
  approvalPolicyId: 'policy-1',
  approvalPolicyVersion: 2,
  createdByUserId: 'creator-1',
  submittedByUserId: 'creator-1',
  submittedAt: '2026-08-01T09:00:00+07:00',
  certifiedAt: null,
  paidAt: null,
  paymentReference: null,
}

describe('usePaymentCertificateActions', () => {
  beforeEach(() => {
    vi.mocked(api.submitPaymentCertificate).mockReset()
    vi.mocked(api.approvePaymentCertificate).mockReset()
    vi.mocked(api.returnPaymentCertificateForRevision).mockReset()
    vi.mocked(api.rejectPaymentCertificate).mockReset()
    vi.mocked(api.recordPayment).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  it('starts idle with no busy action, error, or quorum notice', () => {
    const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))
    expect(result.current.busyAction).toBeNull()
    expect(result.current.actionError).toBeNull()
    expect(result.current.quorumPendingNotice).toBe(false)
  })

  describe('approve', () => {
    it('a step that ADVANCES calls onUpdated and raises a normal success toast (no quorum notice)', async () => {
      const advanced: PaymentCertificateDto = { ...baseCertificate, currentStepNo: 2 }
      vi.mocked(api.approvePaymentCertificate).mockResolvedValueOnce(advanced)
      const onUpdated = vi.fn()

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, onUpdated))

      await act(async () => {
        await result.current.approve('เรียบร้อย')
      })

      expect(onUpdated).toHaveBeenCalledWith(advanced)
      expect(result.current.quorumPendingNotice).toBe(false)
      expect(useToastStore.getState().toasts).toHaveLength(1)
      expect(useToastStore.getState().toasts[0].message).toContain('อนุมัติขั้นตอนที่ 1')
    })

    it('a QUORUM-2 step whose first signature leaves status/step UNCHANGED sets quorumPendingNotice and raises an honest toast, never a generic success', async () => {
      // The exact scenario the security review's §9.5(ii) informational finding describes: a first
      // approver on a 2-of-2 step gets success=true with an unchanged status — must not read as a
      // silent no-op.
      const unchanged: PaymentCertificateDto = { ...baseCertificate }
      vi.mocked(api.approvePaymentCertificate).mockResolvedValueOnce(unchanged)
      const onUpdated = vi.fn()

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, onUpdated))

      await act(async () => {
        await result.current.approve()
      })

      expect(onUpdated).toHaveBeenCalledWith(unchanged)
      expect(result.current.quorumPendingNotice).toBe(true)
      expect(useToastStore.getState().toasts).toHaveLength(1)
      expect(useToastStore.getState().toasts[0].message).toContain('Quorum ยังไม่ครบ')
    })

    it('the final approval (status -> Certified) raises the Certified-specific toast, not the quorum notice', async () => {
      const finalStep: PaymentCertificateDto = { ...baseCertificate, currentStepNo: 2, totalSteps: 2 }
      const certified: PaymentCertificateDto = { ...finalStep, status: 'Certified', certifiedAt: '2026-08-05T00:00:00+07:00' }
      vi.mocked(api.approvePaymentCertificate).mockResolvedValueOnce(certified)

      const { result } = renderHook(() => usePaymentCertificateActions(finalStep, vi.fn()))

      await act(async () => {
        await result.current.approve()
      })

      expect(result.current.quorumPendingNotice).toBe(false)
      expect(useToastStore.getState().toasts[0].message).toContain('Certified')
    })

    it('sets busyAction to "approve" while in flight and clears it afterwards', async () => {
      let resolvePromise!: (value: PaymentCertificateDto) => void
      vi.mocked(api.approvePaymentCertificate).mockReturnValueOnce(
        new Promise((resolve) => {
          resolvePromise = resolve
        }),
      )

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))

      let pending!: Promise<PaymentCertificateDto | null>
      act(() => {
        pending = result.current.approve()
      })
      expect(result.current.busyAction).toBe('approve')

      await act(async () => {
        resolvePromise(baseCertificate)
        await pending
      })
      expect(result.current.busyAction).toBeNull()
    })

    it('a 403 not-current-step failure surfaces the Thai message and does not call onUpdated', async () => {
      vi.mocked(api.approvePaymentCertificate).mockRejectedValueOnce(
        new api.PaymentApiError('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้', 403),
      )
      const onUpdated = vi.fn()

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, onUpdated))

      await act(async () => {
        await result.current.approve()
      })

      expect(onUpdated).not.toHaveBeenCalled()
      expect(result.current.actionError).toBe('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้')
      expect(result.current.busyAction).toBeNull()
    })

    it('does nothing when there is no certificate to act on', async () => {
      const { result } = renderHook(() => usePaymentCertificateActions(null, vi.fn()))
      await act(async () => {
        await result.current.approve()
      })
      expect(api.approvePaymentCertificate).not.toHaveBeenCalled()
    })
  })

  describe('returnForRevision', () => {
    it('calls the API with the mandatory comment and announces the new revision', async () => {
      const returned: PaymentCertificateDto = {
        ...baseCertificate,
        status: 'Draft',
        revisionNo: 2,
        currentStepNo: 0,
        totalSteps: 0,
      }
      vi.mocked(api.returnPaymentCertificateForRevision).mockResolvedValueOnce(returned)
      const onUpdated = vi.fn()

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, onUpdated))

      await act(async () => {
        await result.current.returnForRevision('ยอดไม่ตรงกับ BOQ')
      })

      expect(api.returnPaymentCertificateForRevision).toHaveBeenCalledWith('cert-1', 'ยอดไม่ตรงกับ BOQ')
      expect(onUpdated).toHaveBeenCalledWith(returned)
      expect(useToastStore.getState().toasts[0].message).toContain('revision 2')
    })
  })

  describe('reject', () => {
    it('calls the API and announces the terminal, irreversible outcome', async () => {
      const rejected: PaymentCertificateDto = { ...baseCertificate, status: 'Rejected' }
      vi.mocked(api.rejectPaymentCertificate).mockResolvedValueOnce(rejected)

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))

      await act(async () => {
        await result.current.reject('ยกเลิกงวดนี้ถาวร')
      })

      expect(api.rejectPaymentCertificate).toHaveBeenCalledWith('cert-1', 'ยกเลิกงวดนี้ถาวร')
      expect(useToastStore.getState().toasts[0].message).toContain('ไม่สามารถย้อนกลับได้')
    })
  })

  describe('recordPayment', () => {
    it('calls the API with reference and paidAt', async () => {
      const paid: PaymentCertificateDto = { ...baseCertificate, status: 'Paid', paymentReference: 'CHQ-1' }
      vi.mocked(api.recordPayment).mockResolvedValueOnce(paid)

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))

      await act(async () => {
        await result.current.recordPayment('CHQ-1', '2026-08-10T00:00:00+07:00')
      })

      expect(api.recordPayment).toHaveBeenCalledWith('cert-1', 'CHQ-1', '2026-08-10T00:00:00+07:00')
    })
  })

  describe('submit', () => {
    it('calls the API and announces the resolved chain length', async () => {
      const submitted: PaymentCertificateDto = { ...baseCertificate, status: 'PendingApproval', currentStepNo: 1, totalSteps: 3 }
      vi.mocked(api.submitPaymentCertificate).mockResolvedValueOnce(submitted)

      const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))

      await act(async () => {
        await result.current.submit()
      })

      expect(api.submitPaymentCertificate).toHaveBeenCalledWith('cert-1')
      expect(useToastStore.getState().toasts[0].message).toContain('1/3')
    })
  })

  it('clearActionError resets actionError to null', async () => {
    vi.mocked(api.approvePaymentCertificate).mockRejectedValueOnce(new api.PaymentApiError('ผิดพลาด', 400))
    const { result } = renderHook(() => usePaymentCertificateActions(baseCertificate, vi.fn()))

    await act(async () => {
      await result.current.approve()
    })
    expect(result.current.actionError).toBe('ผิดพลาด')

    act(() => {
      result.current.clearActionError()
    })
    expect(result.current.actionError).toBeNull()
  })
})
