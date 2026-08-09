import { describe, expect, it } from 'vitest'
import {
  canAttemptApprove,
  canAttemptReject,
  canAttemptReturnForRevision,
  resolveChainStepTone,
} from './chainPermissions'
import type { PaymentCertificateDto } from './types'

function makeCertificate(overrides: Partial<PaymentCertificateDto> = {}): PaymentCertificateDto {
  return {
    id: 'cert-1',
    projectId: 'project-1',
    milestoneNo: 1,
    description: null,
    milestoneValue: '1000000.00',
    previousCumulativeApprovePct: '0.00',
    approvePct: '100.00',
    claimPct: null,
    actualProgressPct: null,
    grossCertifiedAmount: '1000000.00',
    retentionAmount: '50000.00',
    advanceRecoveryAmount: '100000.00',
    netPayment: '850000.00',
    status: 'PendingApproval',
    revisionNo: 1,
    currentStepNo: 2,
    totalSteps: 3,
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    createdByUserId: 'creator-1',
    submittedByUserId: 'submitter-1',
    submittedAt: '2026-08-01T00:00:00+07:00',
    certifiedAt: null,
    paidAt: null,
    paymentReference: null,
    ...overrides,
  }
}

describe('canAttemptApprove', () => {
  it('false when the document is not PendingApproval', () => {
    expect(canAttemptApprove(makeCertificate({ status: 'Draft' }), 'someone-else')).toBe(false)
    expect(canAttemptApprove(makeCertificate({ status: 'Certified' }), 'someone-else')).toBe(false)
  })

  it('true for an unrelated authenticated user while PendingApproval', () => {
    expect(canAttemptApprove(makeCertificate(), 'someone-else')).toBe(true)
  })

  it('false for the creator (conservative self-approval guard)', () => {
    expect(canAttemptApprove(makeCertificate(), 'creator-1')).toBe(false)
  })

  it('false for the submitter (conservative self-approval guard)', () => {
    expect(canAttemptApprove(makeCertificate(), 'submitter-1')).toBe(false)
  })

  it('true when there is no known current user id (never crashes on missing claims)', () => {
    expect(canAttemptApprove(makeCertificate(), null)).toBe(true)
  })
})

describe('canAttemptReturnForRevision', () => {
  it('true whenever PendingApproval, regardless of step position', () => {
    expect(canAttemptReturnForRevision(makeCertificate({ currentStepNo: 1, totalSteps: 3 }))).toBe(true)
    expect(canAttemptReturnForRevision(makeCertificate({ currentStepNo: 3, totalSteps: 3 }))).toBe(true)
  })

  it('false outside PendingApproval', () => {
    expect(canAttemptReturnForRevision(makeCertificate({ status: 'Draft' }))).toBe(false)
    expect(canAttemptReturnForRevision(makeCertificate({ status: 'Rejected' }))).toBe(false)
  })
})

describe('canAttemptReject', () => {
  it('false when not at the structurally-final step', () => {
    expect(canAttemptReject(makeCertificate({ currentStepNo: 2, totalSteps: 3 }))).toBe(false)
  })

  it('true only at the final step while PendingApproval', () => {
    expect(canAttemptReject(makeCertificate({ currentStepNo: 3, totalSteps: 3 }))).toBe(true)
  })

  it('false outside PendingApproval even at the final step number', () => {
    expect(canAttemptReject(makeCertificate({ status: 'Certified', currentStepNo: 3, totalSteps: 3 }))).toBe(false)
  })

  it('false when totalSteps is 0 (no chain attached yet)', () => {
    expect(canAttemptReject(makeCertificate({ currentStepNo: 0, totalSteps: 0 }))).toBe(false)
  })
})

describe('resolveChainStepTone', () => {
  it('marks steps before currentStepNo as done, the current one as current, later ones as pending', () => {
    const certificate = makeCertificate({ currentStepNo: 2, totalSteps: 3, status: 'PendingApproval' })
    expect(resolveChainStepTone(certificate, 1)).toBe('done')
    expect(resolveChainStepTone(certificate, 2)).toBe('current')
    expect(resolveChainStepTone(certificate, 3)).toBe('pending')
  })

  it('marks every step done once Certified or Paid', () => {
    const certified = makeCertificate({ status: 'Certified', currentStepNo: 3, totalSteps: 3 })
    expect([1, 2, 3].map((n) => resolveChainStepTone(certified, n))).toEqual(['done', 'done', 'done'])

    const paid = makeCertificate({ status: 'Paid', currentStepNo: 3, totalSteps: 3 })
    expect([1, 2, 3].map((n) => resolveChainStepTone(paid, n))).toEqual(['done', 'done', 'done'])
  })

  it('marks the final step rejected and earlier steps done when Rejected', () => {
    const rejected = makeCertificate({ status: 'Rejected', currentStepNo: 3, totalSteps: 3 })
    expect([1, 2, 3].map((n) => resolveChainStepTone(rejected, n))).toEqual(['done', 'done', 'rejected'])
  })
})
