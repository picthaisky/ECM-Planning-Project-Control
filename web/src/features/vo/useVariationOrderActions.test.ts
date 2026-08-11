import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useVariationOrderActions } from './useVariationOrderActions'
import * as api from './api'
import { useToastStore } from '../../store/toastStore'
import type { VariationOrderDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    submitVariationOrder: vi.fn(),
    approveVariationOrder: vi.fn(),
    returnVariationOrderForRevision: vi.fn(),
    rejectVariationOrder: vi.fn(),
    withdrawVariationOrder: vi.fn(),
    cancelVariationOrder: vi.fn(),
  }
})

const baseVo: VariationOrderDto = {
  id: 'vo-1',
  projectId: 'project-1',
  voNumber: 'VO-018',
  description: 'งานเพิ่มกันสาดอลูมิเนียมทางเข้าหลัก',
  justification: null,
  amount: '2400000.00',
  type: 'Add',
  timeImpactDays: 0,
  status: 'PendingApproval',
  revisionNo: 1,
  currentStepNo: 1,
  totalSteps: 2,
  approvalPolicyId: 'policy-1',
  approvalPolicyVersion: 1,
  allowSelfApproval: false,
  approvalSteps: [
    { stepNo: 1, requiredRole: 'ProjectDirector', quorumCount: 2 },
    { stepNo: 2, requiredRole: 'Executive', quorumCount: 1 },
  ],
  scopeItems: [],
  createdByUserId: 'creator-1',
  submittedByUserId: 'creator-1',
  submittedAt: '2026-08-10T09:00:00+07:00',
  approvedAt: null,
  bacBefore: null,
  bacAfter: null,
  contractValueBefore: null,
  contractValueAfter: null,
  cumulativeVoPctAtApproval: null,
  escalationBasisContractValue: null,
}

describe('useVariationOrderActions', () => {
  beforeEach(() => {
    vi.mocked(api.submitVariationOrder).mockReset()
    vi.mocked(api.approveVariationOrder).mockReset()
    vi.mocked(api.returnVariationOrderForRevision).mockReset()
    vi.mocked(api.rejectVariationOrder).mockReset()
    vi.mocked(api.withdrawVariationOrder).mockReset()
    vi.mocked(api.cancelVariationOrder).mockReset()
    useToastStore.setState({ toasts: [] })
  })

  it('starts idle with no busy action, error, or quorum notice', () => {
    const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
    expect(result.current.busyAction).toBeNull()
    expect(result.current.actionError).toBeNull()
    expect(result.current.quorumPendingNotice).toBe(false)
    expect(result.current.quorumPendingMessage).toBeNull()
    expect(result.current.quorumPendingAction).toBeNull()
  })

  describe('approve', () => {
    it('a step that ADVANCES calls onUpdated and raises a normal success toast (no quorum notice)', async () => {
      const advanced: VariationOrderDto = { ...baseVo, currentStepNo: 2 }
      vi.mocked(api.approveVariationOrder).mockResolvedValueOnce(advanced)
      const onUpdated = vi.fn()

      const { result } = renderHook(() => useVariationOrderActions(baseVo, onUpdated))
      await act(async () => {
        await result.current.approve('เรียบร้อย')
      })

      expect(onUpdated).toHaveBeenCalledWith(advanced)
      expect(result.current.quorumPendingNotice).toBe(false)
      expect(useToastStore.getState().toasts[0].message).toContain('อนุมัติขั้นตอนที่ 1')
    })

    it('a QUORUM-2 step whose first signature leaves status/step UNCHANGED sets quorumPendingNotice with an approve-flavoured message', async () => {
      const unchanged: VariationOrderDto = { ...baseVo }
      vi.mocked(api.approveVariationOrder).mockResolvedValueOnce(unchanged)

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.approve()
      })

      expect(result.current.quorumPendingNotice).toBe(true)
      expect(result.current.quorumPendingMessage).toContain('Quorum ยังไม่ครบ')
      expect(result.current.quorumPendingMessage).toContain('รออนุมัติ')
      expect(result.current.quorumPendingAction).toBe('approve')
    })

    it('the final approval (status -> Approved) raises the Approved-specific toast, not the quorum notice', async () => {
      const finalStep: VariationOrderDto = { ...baseVo, currentStepNo: 2, totalSteps: 2 }
      const approved: VariationOrderDto = { ...finalStep, status: 'Approved', approvedAt: '2026-08-11T00:00:00+07:00' }
      vi.mocked(api.approveVariationOrder).mockResolvedValueOnce(approved)

      const { result } = renderHook(() => useVariationOrderActions(finalStep, vi.fn()))
      await act(async () => {
        await result.current.approve()
      })

      expect(result.current.quorumPendingNotice).toBe(false)
      expect(useToastStore.getState().toasts[0].message).toContain('ปรับ BAC')
    })

    it('does nothing when there is no VO to act on', async () => {
      const { result } = renderHook(() => useVariationOrderActions(null, vi.fn()))
      await act(async () => {
        await result.current.approve()
      })
      expect(api.approveVariationOrder).not.toHaveBeenCalled()
    })
  })

  describe('reject — ADR-0016 quorum binds rejection too', () => {
    it('a QUORUM-2 step whose first rejection leaves status/step UNCHANGED sets quorumPendingNotice and explicitly says the document is NOT rejected', async () => {
      // The exact scenario ADR-0016 exists to fix: today a single reject on a 2-of-2 step would
      // read as terminal; the UI must say the opposite.
      const unchanged: VariationOrderDto = { ...baseVo }
      vi.mocked(api.rejectVariationOrder).mockResolvedValueOnce(unchanged)

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.reject('ไม่เห็นด้วยกับราคา')
      })

      expect(result.current.quorumPendingNotice).toBe(true)
      expect(result.current.quorumPendingMessage).toContain('ยังไม่ถูกปฏิเสธ')
      expect(result.current.quorumPendingMessage).toContain('Quorum')
      expect(result.current.quorumPendingAction).toBe('reject')
      expect(useToastStore.getState().toasts[0].message).toContain('ยังไม่ถูกปฏิเสธ')
    })

    it('the quorum-satisfying rejection (status -> Rejected) raises the terminal toast, not the quorum notice', async () => {
      const finalStep: VariationOrderDto = { ...baseVo, currentStepNo: 2, totalSteps: 2 }
      const rejected: VariationOrderDto = { ...finalStep, status: 'Rejected' }
      vi.mocked(api.rejectVariationOrder).mockResolvedValueOnce(rejected)

      const { result } = renderHook(() => useVariationOrderActions(finalStep, vi.fn()))
      await act(async () => {
        await result.current.reject('ไม่อนุมัติ')
      })

      expect(result.current.quorumPendingNotice).toBe(false)
      expect(useToastStore.getState().toasts[0].message).toContain('ไม่สามารถย้อนกลับได้')
    })

    it('QuorumCount = 1 (the default/common case) is unaffected — a single reject terminates immediately', async () => {
      const singleQuorumStep: VariationOrderDto = {
        ...baseVo,
        currentStepNo: 2,
        totalSteps: 2,
        approvalSteps: [
          { stepNo: 1, requiredRole: 'ProjectDirector', quorumCount: 1 },
          { stepNo: 2, requiredRole: 'Executive', quorumCount: 1 },
        ],
      }
      const rejected: VariationOrderDto = { ...singleQuorumStep, status: 'Rejected' }
      vi.mocked(api.rejectVariationOrder).mockResolvedValueOnce(rejected)

      const { result } = renderHook(() => useVariationOrderActions(singleQuorumStep, vi.fn()))
      await act(async () => {
        await result.current.reject('ไม่อนุมัติ')
      })

      expect(result.current.quorumPendingNotice).toBe(false)
    })

    it('calls the API with the mandatory comment', async () => {
      vi.mocked(api.rejectVariationOrder).mockResolvedValueOnce({ ...baseVo, status: 'Rejected' })

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.reject('เหตุผลการปฏิเสธ')
      })

      expect(api.rejectVariationOrder).toHaveBeenCalledWith('vo-1', 'เหตุผลการปฏิเสธ')
    })
  })

  describe('returnForRevision', () => {
    it('calls the API with the mandatory comment and announces the new revision is resubmittable', async () => {
      const returned: VariationOrderDto = { ...baseVo, status: 'Draft', revisionNo: 2, currentStepNo: 0, totalSteps: 0 }
      vi.mocked(api.returnVariationOrderForRevision).mockResolvedValueOnce(returned)
      const onUpdated = vi.fn()

      const { result } = renderHook(() => useVariationOrderActions(baseVo, onUpdated))
      await act(async () => {
        await result.current.returnForRevision('ยอดไม่ตรงกับ BOQ')
      })

      expect(api.returnVariationOrderForRevision).toHaveBeenCalledWith('vo-1', 'ยอดไม่ตรงกับ BOQ')
      expect(onUpdated).toHaveBeenCalledWith(returned)
      expect(useToastStore.getState().toasts[0].message).toContain('revision 2')
      expect(useToastStore.getState().toasts[0].message).toContain('ส่งใหม่ได้')
    })
  })

  describe('withdraw', () => {
    it('calls the API and announces the return to Draft', async () => {
      const withdrawn: VariationOrderDto = { ...baseVo, status: 'Draft', currentStepNo: 0, totalSteps: 0 }
      vi.mocked(api.withdrawVariationOrder).mockResolvedValueOnce(withdrawn)

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.withdraw()
      })

      expect(api.withdrawVariationOrder).toHaveBeenCalledWith('vo-1')
      expect(useToastStore.getState().toasts[0].message).toContain('ถอนคำขอ')
    })
  })

  describe('cancel', () => {
    it('calls the API with the mandatory comment', async () => {
      const cancelled: VariationOrderDto = { ...baseVo, status: 'Cancelled' }
      vi.mocked(api.cancelVariationOrder).mockResolvedValueOnce(cancelled)

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.cancel('ไม่ดำเนินการต่อ')
      })

      expect(api.cancelVariationOrder).toHaveBeenCalledWith('vo-1', 'ไม่ดำเนินการต่อ')
    })
  })

  describe('submit', () => {
    it('calls the API and announces the resolved chain length', async () => {
      const submitted: VariationOrderDto = { ...baseVo, status: 'PendingApproval', currentStepNo: 1, totalSteps: 3 }
      vi.mocked(api.submitVariationOrder).mockResolvedValueOnce(submitted)

      const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))
      await act(async () => {
        await result.current.submit()
      })

      expect(api.submitVariationOrder).toHaveBeenCalledWith('vo-1')
      expect(useToastStore.getState().toasts[0].message).toContain('1/3')
    })
  })

  it('sets busyAction to the in-flight action kind and clears it afterwards', async () => {
    let resolvePromise!: (value: VariationOrderDto) => void
    vi.mocked(api.approveVariationOrder).mockReturnValueOnce(
      new Promise((resolve) => {
        resolvePromise = resolve
      }),
    )

    const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))

    let pending!: Promise<VariationOrderDto | null>
    act(() => {
      pending = result.current.approve()
    })
    expect(result.current.busyAction).toBe('approve')

    await act(async () => {
      resolvePromise(baseVo)
      await pending
    })
    expect(result.current.busyAction).toBeNull()
  })

  it('a 403 failure surfaces the Thai message and does not call onUpdated', async () => {
    vi.mocked(api.approveVariationOrder).mockRejectedValueOnce(
      new api.VoApiError('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้', 403),
    )
    const onUpdated = vi.fn()

    const { result } = renderHook(() => useVariationOrderActions(baseVo, onUpdated))
    await act(async () => {
      await result.current.approve()
    })

    expect(onUpdated).not.toHaveBeenCalled()
    expect(result.current.actionError).toBe('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้')
  })

  it('clearActionError resets actionError to null', async () => {
    vi.mocked(api.approveVariationOrder).mockRejectedValueOnce(new api.VoApiError('ผิดพลาด', 400))
    const { result } = renderHook(() => useVariationOrderActions(baseVo, vi.fn()))

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
