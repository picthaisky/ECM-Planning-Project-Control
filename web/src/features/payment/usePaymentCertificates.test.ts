import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePaymentCertificates } from './usePaymentCertificates'
import * as api from './api'
import type { PaymentCertificateDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    listPaymentCertificates: vi.fn(),
    getPaymentCertificate: vi.fn(),
  }
})

function makeCertificate(overrides: Partial<PaymentCertificateDto> = {}): PaymentCertificateDto {
  return {
    id: 'cert-1',
    projectId: 'project-1',
    milestoneNo: 1,
    description: null,
    milestoneValue: '1000000.00',
    previousCumulativeApprovePct: '0.00',
    approvePct: '50.00',
    claimPct: null,
    actualProgressPct: null,
    grossCertifiedAmount: '500000.00',
    retentionAmount: '25000.00',
    advanceRecoveryAmount: '50000.00',
    netPayment: '425000.00',
    status: 'Draft',
    revisionNo: 1,
    currentStepNo: 0,
    totalSteps: 0,
    approvalPolicyId: null,
    approvalPolicyVersion: null,
    createdByUserId: 'user-1',
    submittedByUserId: null,
    submittedAt: null,
    certifiedAt: null,
    paidAt: null,
    paymentReference: null,
    ...overrides,
  }
}

describe('usePaymentCertificates', () => {
  beforeEach(() => {
    vi.mocked(api.listPaymentCertificates).mockReset()
    vi.mocked(api.getPaymentCertificate).mockReset()
  })

  it('loads the list on mount and auto-selects the first certificate', async () => {
    const first = makeCertificate({ id: 'cert-1' })
    const second = makeCertificate({ id: 'cert-2' })
    vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([first, second])

    const { result } = renderHook(() => usePaymentCertificates('project-1'))

    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.certificates).toEqual([first, second])
    expect(result.current.selected).toEqual(first)
  })

  it('empty state: an empty list resolves to ready with no selection, not an error', async () => {
    vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([])

    const { result } = renderHook(() => usePaymentCertificates('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.certificates).toEqual([])
    expect(result.current.selected).toBeNull()
  })

  it('error state: a failed load surfaces the Thai message from PaymentApiError', async () => {
    vi.mocked(api.listPaymentCertificates).mockRejectedValueOnce(
      new api.PaymentApiError('ไม่พบข้อมูลที่ระบุ', 404),
    )

    const { result } = renderHook(() => usePaymentCertificates('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('ไม่พบข้อมูลที่ระบุ')
  })

  it('select() switches the current selection among already-loaded certificates', async () => {
    const first = makeCertificate({ id: 'cert-1' })
    const second = makeCertificate({ id: 'cert-2' })
    vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([first, second])

    const { result } = renderHook(() => usePaymentCertificates('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.select('cert-2'))
    expect(result.current.selected).toEqual(second)
  })

  it('applyUpdatedCertificate patches an existing row and keeps it selected', async () => {
    const first = makeCertificate({ id: 'cert-1', status: 'Draft' })
    vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([first])

    const { result } = renderHook(() => usePaymentCertificates('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    const updated = makeCertificate({ id: 'cert-1', status: 'PendingApproval', currentStepNo: 1, totalSteps: 2 })
    act(() => result.current.applyUpdatedCertificate(updated))

    expect(result.current.certificates).toEqual([updated])
    expect(result.current.selected).toEqual(updated)
  })

  describe('reloadSelected', () => {
    it('calls getPaymentCertificate for the selected id and patches it in on success', async () => {
      const first = makeCertificate({ id: 'cert-1', status: 'Draft' })
      vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([first])

      const { result } = renderHook(() => usePaymentCertificates('project-1'))
      await waitFor(() => expect(result.current.loadState).toBe('ready'))

      const reloaded = makeCertificate({ id: 'cert-1', status: 'PendingApproval' })
      vi.mocked(api.getPaymentCertificate).mockResolvedValueOnce(reloaded)

      await act(async () => {
        await result.current.reloadSelected()
      })

      expect(api.getPaymentCertificate).toHaveBeenCalledWith('cert-1')
      expect(result.current.selected).toEqual(reloaded)
      expect(result.current.reloadState).toBe('idle')
    })

    it('surfaces a Thai error and leaves the existing selection untouched on failure', async () => {
      const first = makeCertificate({ id: 'cert-1' })
      vi.mocked(api.listPaymentCertificates).mockResolvedValueOnce([first])

      const { result } = renderHook(() => usePaymentCertificates('project-1'))
      await waitFor(() => expect(result.current.loadState).toBe('ready'))

      vi.mocked(api.getPaymentCertificate).mockRejectedValueOnce(new api.PaymentApiError('ไม่พบข้อมูล', 404))

      await act(async () => {
        await result.current.reloadSelected()
      })

      expect(result.current.reloadState).toBe('error')
      expect(result.current.reloadError).toBe('ไม่พบข้อมูล')
      expect(result.current.selected).toEqual(first)
    })
  })
})
