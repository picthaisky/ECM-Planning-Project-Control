import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  approveVariationOrder,
  cancelVariationOrder,
  createVariationOrder,
  getVariationOrder,
  getVoApprovalActions,
  listVariationOrders,
  rejectVariationOrder,
  returnVariationOrderForRevision,
  submitVariationOrder,
  updateVariationOrderContent,
  withdrawVariationOrder,
} from './api'
import { apiClient } from '../../services/apiClient'
import type { ApprovalActionDto, VariationOrderDto } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}))

function makeConfig(url: string): InternalAxiosRequestConfig {
  return { url, headers: new AxiosHeaders() } as InternalAxiosRequestConfig
}

function makeError(status: number, data: unknown): AxiosError {
  const config = makeConfig('/x')
  return new AxiosError('Request failed', String(status), config, undefined, {
    status,
    statusText: '',
    data,
    headers: {},
    config,
  })
}

const sampleVo: VariationOrderDto = {
  id: 'vo-1',
  projectId: 'project-1',
  voNumber: 'VO-018',
  description: 'งานเพิ่มกันสาดอลูมิเนียมทางเข้าหลัก',
  justification: 'เจ้าของโครงการร้องขอเพิ่มเติม',
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
    { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
    { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
  ],
  scopeItems: [{ activityId: 'activity-1', budgetCostDelta: '2400000.00', note: null }],
  createdByUserId: 'user-1',
  submittedByUserId: 'user-1',
  submittedAt: '2026-08-10T09:00:00+07:00',
  approvedAt: null,
  bacBefore: null,
  bacAfter: null,
  contractValueBefore: null,
  contractValueAfter: null,
  cumulativeVoPctAtApproval: null,
  escalationBasisContractValue: null,
}

describe('features/vo/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
    vi.mocked(apiClient.put).mockReset()
  })

  describe('listVariationOrders', () => {
    it('fetches the project-scoped VO register', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleVo] })

      const result = await listVariationOrders('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/variation-orders')
      expect(result).toEqual([sampleVo])
    })
  })

  describe('getVariationOrder', () => {
    it('fetches a single VO by id', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleVo })

      const result = await getVariationOrder('vo-1')

      expect(apiClient.get).toHaveBeenCalledWith('/variation-orders/vo-1')
      expect(result).toEqual(sampleVo)
    })
  })

  describe('getVoApprovalActions', () => {
    it('fetches the approval-action history for a VO', async () => {
      const history: ApprovalActionDto[] = [
        {
          id: 'action-1',
          documentType: 'VariationOrder',
          documentId: 'vo-1',
          revisionNo: 1,
          stepNo: 1,
          actorUserId: 'user-2',
          actorRoleAtTime: 'PM',
          action: 'Approve',
          comment: null,
          actedAt: '2026-08-10T10:00:00+07:00',
          approvalPolicyId: 'policy-1',
          approvalPolicyVersion: 1,
        },
      ]
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: history })

      const result = await getVoApprovalActions('vo-1')

      expect(apiClient.get).toHaveBeenCalledWith('/variation-orders/vo-1/approval-actions')
      expect(result).toEqual(history)
    })

    it('translates the (currently unconditional) 404 to a Thai error rather than throwing raw', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'PaymentApprovalNotFound' }),
      )

      await expect(getVoApprovalActions('vo-1')).rejects.toMatchObject({ name: 'VoApiError', status: 404 })
    })
  })

  describe('createVariationOrder', () => {
    it('posts the create payload to the project-scoped endpoint', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleVo })

      const payload = {
        voNumber: 'VO-018',
        description: 'งานเพิ่มกันสาดอลูมิเนียมทางเข้าหลัก',
        justification: null,
        amount: '2400000.00',
        timeImpactDays: 0,
        scopeItems: [{ activityId: 'activity-1', budgetCostDelta: '2400000.00', note: null }],
      }

      const result = await createVariationOrder('project-1', payload)

      expect(apiClient.post).toHaveBeenCalledWith('/projects/project-1/variation-orders', payload)
      expect(result).toEqual(sampleVo)
    })

    it('translates a duplicate VoNumber (409) to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, {
          type: 'https://cmplus.dev/problems/duplicate-vo-number',
          detail: 'VariationOrderDuplicateVoNumber',
        }),
      )

      await expect(
        createVariationOrder('project-1', {
          voNumber: 'VO-018',
          description: null,
          justification: null,
          amount: '0.00',
          timeImpactDays: 1,
          scopeItems: [],
        }),
      ).rejects.toMatchObject({
        message: 'เลขที่ VO นี้ถูกใช้แล้วในโครงการนี้ กรุณาระบุเลขที่อื่น',
        status: 409,
      })
    })

    it('translates VoScopeBudgetMismatch (422) to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, { type: 'https://cmplus.dev/problems/vo-scope-budget-mismatch', detail: 'VoScopeBudgetMismatch' }),
      )

      await expect(
        createVariationOrder('project-1', {
          voNumber: 'VO-019',
          description: null,
          justification: null,
          amount: '100.00',
          timeImpactDays: 0,
          scopeItems: [],
        }),
      ).rejects.toMatchObject({
        message: 'ผลรวมมูลค่างานที่ปรับ (Scope) ต้องเท่ากับมูลค่า VO พอดี กรุณาตรวจสอบตัวเลข',
      })
    })
  })

  describe('updateVariationOrderContent', () => {
    it('puts the content payload to the id-scoped content endpoint', async () => {
      vi.mocked(apiClient.put).mockResolvedValueOnce({ data: sampleVo })

      const payload = {
        description: 'แก้ไขแล้ว',
        justification: null,
        amount: '2400000.00',
        timeImpactDays: 0,
        scopeItems: [{ activityId: 'activity-1', budgetCostDelta: '2400000.00', note: null }],
      }

      await updateVariationOrderContent('vo-1', payload)

      expect(apiClient.put).toHaveBeenCalledWith('/variation-orders/vo-1/content', payload)
    })
  })

  describe('submitVariationOrder', () => {
    it('posts to the real submit endpoint with no body', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleVo })

      const result = await submitVariationOrder('vo-1')

      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/submit')
      expect(result).toEqual(sampleVo)
    })

    it('translates ApprovalPolicyGap (422, fail-closed) to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, { type: 'https://cmplus.dev/problems/approval-policy-gap', detail: 'ApprovalPolicyGap' }),
      )

      await expect(submitVariationOrder('vo-1')).rejects.toMatchObject({
        message: 'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',
        status: 422,
      })
    })
  })

  describe('approveVariationOrder', () => {
    it('sends a trimmed comment, or null when omitted/blank', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleVo })
      await approveVariationOrder('vo-1')
      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/approve', { comment: null })

      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleVo })
      await approveVariationOrder('vo-1', '  เรียบร้อย  ')
      expect(apiClient.post).toHaveBeenLastCalledWith('/variation-orders/vo-1/approve', { comment: 'เรียบร้อย' })
    })

    it('translates the escalation-bypass 409 to an actionable Thai message', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, {
          type: 'https://cmplus.dev/problems/vo-escalation-threshold-crossed-since-submission',
          detail: 'VoEscalationThresholdCrossedSinceSubmission',
        }),
      )

      await expect(approveVariationOrder('vo-1')).rejects.toMatchObject({
        message: expect.stringContaining('ตีกลับแก้ไข'),
        status: 409,
      })
    })

    it('translates duplicate-chain-voter (ADR-0016, Approve-or-Reject widened) to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(403, {
          type: 'https://cmplus.dev/problems/duplicate-chain-voter',
          detail: 'VariationOrderDuplicateChainVoter',
        }),
      )

      await expect(approveVariationOrder('vo-1')).rejects.toMatchObject({
        message: 'คุณได้อนุมัติหรือปฏิเสธขั้นตอนอื่นของเอกสารฉบับนี้ไปแล้ว ไม่สามารถลงมติซ้ำได้',
      })
    })
  })

  describe('returnVariationOrderForRevision', () => {
    it('sends the mandatory comment to the return-for-revision endpoint', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleVo, status: 'Draft', revisionNo: 2 } })

      await returnVariationOrderForRevision('vo-1', 'ราคาต่อหน่วยไม่ตรงกับ BOQ')

      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/return-for-revision', {
        comment: 'ราคาต่อหน่วยไม่ตรงกับ BOQ',
      })
    })
  })

  describe('rejectVariationOrder', () => {
    it('posts the mandatory comment to the reject endpoint', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleVo, status: 'Rejected' } })

      await rejectVariationOrder('vo-1', 'ไม่อนุมัติเนื่องจากซ้ำซ้อนกับ VO-014')

      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/reject', {
        comment: 'ไม่อนุมัติเนื่องจากซ้ำซ้อนกับ VO-014',
      })
    })
  })

  describe('withdrawVariationOrder', () => {
    it('posts to the withdraw endpoint with no body', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleVo, status: 'Draft' } })

      await withdrawVariationOrder('vo-1')

      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/withdraw')
    })

    it('translates WithdrawAfterVoteCast to its own specific Thai message, not the generic document-immutable one', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, {
          type: 'https://cmplus.dev/problems/document-immutable',
          detail: 'VariationOrderWithdrawAfterVoteCast',
        }),
      )

      await expect(withdrawVariationOrder('vo-1')).rejects.toMatchObject({
        message: 'มีการลงมติอนุมัติ/ปฏิเสธในรอบนี้ไปแล้วอย่างน้อยหนึ่งครั้ง ไม่สามารถถอนคำขอได้อีก',
      })
    })
  })

  describe('cancelVariationOrder', () => {
    it('posts the mandatory comment to the cancel endpoint', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { ...sampleVo, status: 'Cancelled' } })

      await cancelVariationOrder('vo-1', 'ยกเลิกคำขอเปลี่ยนแปลง ไม่ดำเนินการต่อ')

      expect(apiClient.post).toHaveBeenCalledWith('/variation-orders/vo-1/cancel', {
        comment: 'ยกเลิกคำขอเปลี่ยนแปลง ไม่ดำเนินการต่อ',
      })
    })
  })

  it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
    const config = makeConfig('/variation-orders/vo-1/approve')
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
    vi.mocked(apiClient.post).mockRejectedValueOnce(networkError)

    await expect(approveVariationOrder('vo-1')).rejects.toMatchObject({
      name: 'VoApiError',
      message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      status: undefined,
    })
  })
})
