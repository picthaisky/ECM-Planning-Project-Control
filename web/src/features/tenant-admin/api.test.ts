import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApprovalPolicyBandError, getApprovalPolicy, updateApprovalPolicy } from './api'
import { apiClient } from '../../services/apiClient'
import type { ApprovalPolicy, UpdateApprovalPolicyPayload } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn() },
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

const samplePolicy: ApprovalPolicy = {
  documentType: 'PaymentCertificate',
  version: 2,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: [
    { stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 },
    { stepNo: 2, minAmount: '10000000.00', maxAmount: null, requiredRole: 'ProjectDirector', quorumCount: 1 },
  ],
}

describe('features/tenant-admin/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.put).mockReset()
  })

  describe('getApprovalPolicy', () => {
    it('fetches the active policy for a document type', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: samplePolicy })

      const result = await getApprovalPolicy('tenant-1', 'PaymentCertificate')

      expect(apiClient.get).toHaveBeenCalledWith('/tenants/tenant-1/approval-policies', {
        params: { documentType: 'PaymentCertificate' },
      })
      expect(result).toEqual(samplePolicy)
    })

    it('translates a 404 (not configured yet, or cross-tenant) to a Thai "not configured" message', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'ApprovalPolicyNotFound' }),
      )

      await expect(getApprovalPolicy('tenant-1', 'VariationOrder')).rejects.toMatchObject({
        name: 'TenantAdminApiError',
        message: 'ยังไม่มีการตั้งค่านโยบายอนุมัติสำหรับประเภทเอกสารนี้',
        status: 404,
      })
    })

    it('a bodyless 403 (Admin-only role gate) still becomes a well-formed TenantAdminApiError', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(403, ''))

      await expect(getApprovalPolicy('tenant-1', 'PaymentCertificate')).rejects.toMatchObject({
        name: 'TenantAdminApiError',
        status: 403,
      })
    })
  })

  describe('updateApprovalPolicy', () => {
    const payload: UpdateApprovalPolicyPayload = {
      allowSelfApproval: false,
      cumulativeVoEscalationPct: null,
      cumulativeVoEscalationRole: null,
      rules: samplePolicy.rules,
    }

    it('PUTs to the versioned endpoint and returns the new version', async () => {
      vi.mocked(apiClient.put).mockResolvedValueOnce({ data: { ...samplePolicy, version: 3 } })

      const result = await updateApprovalPolicy('tenant-1', 'PaymentCertificate', payload)

      expect(apiClient.put).toHaveBeenCalledWith('/tenants/tenant-1/approval-policies/PaymentCertificate', payload)
      expect(result.version).toBe(3)
    })

    it('translates a BandOverlap 400 into an ApprovalPolicyBandError naming the offending StepNo', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/approval-policy-band-overlap',
          title: 'The approval policy rule bands are invalid.',
          detail: 'ApprovalPolicyBandOverlap:2',
          problem: 'BandOverlap',
          invalidStepNo: 2,
        }),
      )

      await expect(updateApprovalPolicy('tenant-1', 'PaymentCertificate', payload)).rejects.toMatchObject({
        name: 'ApprovalPolicyBandError',
        problem: 'BandOverlap',
        invalidStepNo: 2,
        status: 400,
      })
    })

    it('translates a BandGap 400 into an ApprovalPolicyBandError naming the offending StepNo', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/approval-policy-band-gap',
          detail: 'ApprovalPolicyBandGap:3',
          problem: 'BandGap',
          invalidStepNo: 3,
        }),
      )

      const error = await updateApprovalPolicy('tenant-1', 'PaymentCertificate', payload).catch((e) => e)
      expect(error).toBeInstanceOf(ApprovalPolicyBandError)
      expect(error.problem).toBe('BandGap')
      expect(error.invalidStepNo).toBe(3)
      expect(error.message).toContain('3')
    })

    it('a plain validation-error 400 (no problem/invalidStepNo) falls back to the generic translator', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, { type: 'https://cmplus.dev/problems/validation-error', detail: 'validation-error' }),
      )

      await expect(updateApprovalPolicy('tenant-1', 'PaymentCertificate', payload)).rejects.toMatchObject({
        name: 'TenantAdminApiError',
        message: 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
      })
    })
  })

  it('a genuine network failure becomes the generic Thai error, never a raw/thrown Axios error', async () => {
    const config = makeConfig('/tenants/tenant-1/approval-policies')
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK', config, undefined, undefined)
    vi.mocked(apiClient.get).mockRejectedValueOnce(networkError)

    await expect(getApprovalPolicy('tenant-1', 'PaymentCertificate')).rejects.toMatchObject({
      name: 'TenantAdminApiError',
      message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      status: undefined,
    })
  })
})
