import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  approvePaymentCertificate,
  getApprovalActions,
  getPaymentCertificate,
  listPaymentCertificates,
  recordPayment,
  rejectPaymentCertificate,
  returnPaymentCertificateForRevision,
  submitPaymentCertificate,
} from './api'
import { apiClient } from '../../services/apiClient'
import type { ApprovalActionDto, PaymentCertificateDto } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn() },
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

const sampleCertificate: PaymentCertificateDto = {
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
  totalSteps: 3,
  approvalPolicyId: 'policy-1',
  approvalPolicyVersion: 2,
  createdByUserId: 'user-1',
  submittedByUserId: 'user-1',
  submittedAt: '2026-08-01T09:00:00+07:00',
  certifiedAt: null,
  paidAt: null,
  paymentReference: null,
}

describe('features/payment/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.post).mockReset()
  })

  describe('listPaymentCertificates', () => {
    it('fetches the milestone list for a project', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleCertificate] })

      const result = await listPaymentCertificates('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/payment-certificates')
      expect(result).toEqual([sampleCertificate])
    })

    it('translates a 404 (the endpoint does not exist on the real backend yet) to a Thai error', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'not-found' }),
      )

      await expect(listPaymentCertificates('project-1')).rejects.toMatchObject({
        name: 'PaymentApiError',
        status: 404,
      })
    })
  })

  describe('getPaymentCertificate', () => {
    it('fetches a single certificate by id', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleCertificate })

      const result = await getPaymentCertificate('cert-1')

      expect(apiClient.get).toHaveBeenCalledWith('/payment-certificates/cert-1')
      expect(result).toEqual(sampleCertificate)
    })
  })

  describe('getApprovalActions', () => {
    it('fetches the approval-action history for a certificate', async () => {
      const history: ApprovalActionDto[] = [
        {
          id: 'action-1',
          documentType: 'PaymentCertificate',
          documentId: 'cert-1',
          revisionNo: 1,
          stepNo: 1,
          actorUserId: 'user-2',
          actorRoleAtTime: 'QS',
          action: 'Approve',
          comment: null,
          actedAt: '2026-08-02T10:00:00+07:00',
          approvalPolicyId: 'policy-1',
          approvalPolicyVersion: 2,
        },
      ]
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: history })

      const result = await getApprovalActions('cert-1')

      expect(apiClient.get).toHaveBeenCalledWith('/payment-certificates/cert-1/approval-actions')
      expect(result).toEqual(history)
    })
  })

  describe('submitPaymentCertificate', () => {
    it('posts to the real submit endpoint with no body', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })

      const result = await submitPaymentCertificate('cert-1')

      expect(apiClient.post).toHaveBeenCalledWith('/payment-certificates/cert-1/submit')
      expect(result).toEqual(sampleCertificate)
    })

    it('translates ApprovalPolicyGap (422, fail-closed) to a Thai error naming the policy gap', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, { type: 'https://cmplus.dev/problems/approval-policy-gap', detail: 'ApprovalPolicyGap' }),
      )

      await expect(submitPaymentCertificate('cert-1')).rejects.toMatchObject({
        name: 'PaymentApiError',
        message: 'ไม่พบเส้นทางอนุมัติที่รองรับมูลค่านี้ ไม่สามารถส่งอนุมัติได้ กรุณาแจ้งผู้ดูแลระบบให้ตั้งค่านโยบายอนุมัติ',
        status: 422,
      })
    })
  })

  describe('approvePaymentCertificate', () => {
    it('sends a trimmed comment, or null when omitted/blank', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })
      await approvePaymentCertificate('cert-1')
      expect(apiClient.post).toHaveBeenCalledWith('/payment-certificates/cert-1/approve', { comment: null })

      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })
      await approvePaymentCertificate('cert-1', '   ')
      expect(apiClient.post).toHaveBeenLastCalledWith('/payment-certificates/cert-1/approve', { comment: null })

      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })
      await approvePaymentCertificate('cert-1', '  ดูดีแล้ว  ')
      expect(apiClient.post).toHaveBeenLastCalledWith('/payment-certificates/cert-1/approve', {
        comment: 'ดูดีแล้ว',
      })
    })

    it('translates not-current-step (the "no escape hatch" 403) to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(403, {
          type: 'https://cmplus.dev/problems/not-current-step',
          detail: 'PaymentCertificateNotAuthorizedForApprovalStep',
        }),
      )

      await expect(approvePaymentCertificate('cert-1')).rejects.toMatchObject({
        name: 'PaymentApiError',
        message: 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',
        status: 403,
      })
    })

    it('translates self-approval-not-permitted to a Thai error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(403, {
          type: 'https://cmplus.dev/problems/self-approval-not-permitted',
          detail: 'PaymentCertificateSelfApprovalNotPermitted',
        }),
      )

      await expect(approvePaymentCertificate('cert-1')).rejects.toMatchObject({
        message: 'ผู้สร้างหรือผู้ยื่นเอกสารไม่สามารถอนุมัติเอกสารฉบับของตนเองได้',
      })
    })

    it('translates a 409 concurrent-transition to a Thai error asking the user to reload', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(409, {
          type: 'https://cmplus.dev/problems/concurrent-transition',
          detail: 'PaymentCertificateConcurrencyConflict',
        }),
      )

      await expect(approvePaymentCertificate('cert-1')).rejects.toMatchObject({
        message: 'มีการเปลี่ยนแปลงเอกสารนี้พร้อมกันจากที่อื่น กรุณาโหลดข้อมูลใหม่แล้วลองอีกครั้ง',
        status: 409,
      })
    })
  })

  describe('returnPaymentCertificateForRevision', () => {
    it('sends the mandatory comment verbatim (not trimmed away if blank — validation is the caller/server\'s job)', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })

      await returnPaymentCertificateForRevision('cert-1', 'ยอดไม่ตรงกับ BOQ กรุณาแก้ไข')

      expect(apiClient.post).toHaveBeenCalledWith('/payment-certificates/cert-1/return-for-revision', {
        comment: 'ยอดไม่ตรงกับ BOQ กรุณาแก้ไข',
      })
    })
  })

  describe('rejectPaymentCertificate', () => {
    it('posts to the real reject endpoint with the mandatory comment', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })

      await rejectPaymentCertificate('cert-1', 'ยกเลิกงวดนี้ถาวร')

      expect(apiClient.post).toHaveBeenCalledWith('/payment-certificates/cert-1/reject', {
        comment: 'ยกเลิกงวดนี้ถาวร',
      })
    })
  })

  describe('recordPayment', () => {
    it('posts the reference and paidAt', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleCertificate })

      await recordPayment('cert-1', 'CHQ-00123', '2026-08-10T00:00:00+07:00')

      expect(apiClient.post).toHaveBeenCalledWith('/payment-certificates/cert-1/record-payment', {
        reference: 'CHQ-00123',
        paidAt: '2026-08-10T00:00:00+07:00',
      })
    })
  })

  it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
    const config = makeConfig('/payment-certificates/cert-1/approve')
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
    vi.mocked(apiClient.post).mockRejectedValueOnce(networkError)

    await expect(approvePaymentCertificate('cert-1')).rejects.toMatchObject({
      name: 'PaymentApiError',
      message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      status: undefined,
    })
  })
})
