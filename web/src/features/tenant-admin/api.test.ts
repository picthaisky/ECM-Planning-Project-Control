import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApprovalPolicyBandError,
  ApprovalRoutingSimulationApiError,
  getApprovalPolicy,
  getApprovalPolicyHistory,
  simulateApprovalRouting,
  updateApprovalPolicy,
} from './api'
import { apiClient } from '../../services/apiClient'
import type {
  ApprovalPolicy,
  ApprovalPolicyVersionHistoryEntry,
  ApprovalRoutingSimulation,
  UpdateApprovalPolicyPayload,
} from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn(), post: vi.fn() },
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
    vi.mocked(apiClient.post).mockReset()
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

  describe('getApprovalPolicyHistory', () => {
    const sampleHistory: ApprovalPolicyVersionHistoryEntry[] = [
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
        cumulativeVoEscalationPct: '10.00',
        cumulativeVoEscalationRole: 'Executive',
        ruleCount: 2,
        createdByUserId: 'user-1',
        createdAt: '2026-06-01T00:00:00+07:00',
        lastModifiedByUserId: 'user-1',
        lastModifiedAt: '2026-06-01T00:00:00+07:00',
      },
    ]

    it('fetches the full version timeline (S15-BE-01), no new storage — plain GET, no query params', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleHistory })

      const result = await getApprovalPolicyHistory('tenant-1', 'VariationOrder')

      expect(apiClient.get).toHaveBeenCalledWith('/tenants/tenant-1/approval-policies/VariationOrder/history')
      expect(result).toEqual(sampleHistory)
    })

    it('an empty history is returned as-is, never translated into an error (the handler treats it as legitimate)', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [] })

      await expect(getApprovalPolicyHistory('tenant-1', 'PaymentCertificate')).resolves.toEqual([])
    })

    it('a bodyless 403 (Admin-only role gate) still becomes a well-formed TenantAdminApiError', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(403, ''))

      await expect(getApprovalPolicyHistory('tenant-1', 'VariationOrder')).rejects.toMatchObject({
        name: 'TenantAdminApiError',
        status: 403,
      })
    })
  })

  describe('simulateApprovalRouting', () => {
    const sampleSimulation: ApprovalRoutingSimulation = {
      documentType: 'VariationOrder',
      projectId: 'project-1',
      inputAmount: '2400000.00',
      routingAmount: '2400000.00',
      approvalPolicyId: 'policy-2',
      approvalPolicyVersion: 2,
      usedFallbackChain: false,
      steps: [
        { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
        { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
      ],
      escalationApplied: false,
      allowSelfApproval: false,
      multipleActivePoliciesDetected: false,
      ambiguousActivePolicies: [],
    }

    it('POSTs the hypothetical project/amount and returns the resolved chain (the exact real-Submit path)', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleSimulation })

      const result = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'project-1',
        amount: '2400000.00',
      })

      expect(apiClient.post).toHaveBeenCalledWith('/tenants/tenant-1/approval-policies/VariationOrder/simulate', {
        projectId: 'project-1',
        amount: '2400000.00',
      })
      expect(result).toEqual(sampleSimulation)
    })

    it('passes through the ADR-0021 ambiguity fields untouched — never swallowed or normalized away', async () => {
      const ambiguous: ApprovalRoutingSimulation = {
        ...sampleSimulation,
        multipleActivePoliciesDetected: true,
        ambiguousActivePolicies: [
          { approvalPolicyId: 'policy-2', version: 2 },
          { approvalPolicyId: 'policy-3', version: 3 },
        ],
      }
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: ambiguous })

      const result = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'project-1',
        amount: '2400000.00',
      })

      expect(result.multipleActivePoliciesDetected).toBe(true)
      expect(result.ambiguousActivePolicies).toHaveLength(2)
    })

    it('a 404 ApprovalSimulationProjectNotFound becomes a typed ApprovalRoutingSimulationApiError', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(404, { type: 'https://cmplus.dev/problems/not-found', detail: 'ApprovalSimulationProjectNotFound' }),
      )

      const error = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'missing-project',
        amount: '1.00',
      }).catch((e) => e)

      expect(error).toBeInstanceOf(ApprovalRoutingSimulationApiError)
      expect(error.code).toBe('ProjectNotFound')
      expect(error.status).toBe(404)
    })

    it('a 422 ApprovalPolicyGap becomes a typed error saying the real submission would be blocked', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, { type: 'https://cmplus.dev/problems/approval-policy-gap', detail: 'ApprovalPolicyGap' }),
      )

      const error = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'project-1',
        amount: '999999999.00',
      }).catch((e) => e)

      expect(error).toBeInstanceOf(ApprovalRoutingSimulationApiError)
      expect(error.code).toBe('PolicyGap')
      expect(error.message).toContain('ไม่มีเส้นทางอนุมัติที่ resolve ได้')
    })

    it('a 422 ContractValueNotConfigured becomes a typed error naming the missing baseline contract value', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(422, {
          type: 'https://cmplus.dev/problems/contract-value-not-configured',
          detail: 'ContractValueNotConfigured',
        }),
      )

      const error = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'project-1',
        amount: '2400000.00',
      }).catch((e) => e)

      expect(error).toBeInstanceOf(ApprovalRoutingSimulationApiError)
      expect(error.code).toBe('ContractValueNotConfigured')
      expect(error.message).toContain('มูลค่าสัญญาตั้งต้น')
    })

    it('an unrecognised error code falls back to the generic Thai translator, not a typed simulation error', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(makeError(403, ''))

      const error = await simulateApprovalRouting('tenant-1', 'VariationOrder', {
        projectId: 'project-1',
        amount: '2400000.00',
      }).catch((e) => e)

      expect(error).not.toBeInstanceOf(ApprovalRoutingSimulationApiError)
      expect(error.name).toBe('TenantAdminApiError')
      expect(error.status).toBe(403)
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
