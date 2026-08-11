import { describe, expect, it } from 'vitest'
import {
  canAttemptApprove,
  canAttemptCancel,
  canAttemptEditContent,
  canAttemptReject,
  canAttemptReturnForRevision,
  canAttemptSubmit,
  canAttemptWithdraw,
  resolveChainStepTone,
} from './chainPermissions'
import type { VariationOrderDto } from './types'

function makeVo(overrides: Partial<VariationOrderDto> = {}): VariationOrderDto {
  return {
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
    currentStepNo: 2,
    totalSteps: 3,
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    allowSelfApproval: false,
    approvalSteps: [
      { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
      { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
      { stepNo: 3, requiredRole: 'Executive', quorumCount: 1 },
    ],
    scopeItems: [],
    createdByUserId: 'creator-1',
    submittedByUserId: 'submitter-1',
    submittedAt: '2026-08-01T00:00:00+07:00',
    approvedAt: null,
    bacBefore: null,
    bacAfter: null,
    contractValueBefore: null,
    contractValueAfter: null,
    cumulativeVoPctAtApproval: null,
    escalationBasisContractValue: null,
    ...overrides,
  }
}

describe('canAttemptSubmit / canAttemptEditContent', () => {
  it('true only while Draft (covers both never-submitted and returned-for-revision)', () => {
    expect(canAttemptSubmit(makeVo({ status: 'Draft' }))).toBe(true)
    expect(canAttemptEditContent(makeVo({ status: 'Draft', revisionNo: 2 }))).toBe(true)
  })

  it('false once submitted or terminal', () => {
    for (const status of ['PendingApproval', 'Approved', 'Rejected', 'Cancelled'] as const) {
      expect(canAttemptSubmit(makeVo({ status }))).toBe(false)
      expect(canAttemptEditContent(makeVo({ status }))).toBe(false)
    }
  })
})

describe('canAttemptApprove', () => {
  it('false when the document is not PendingApproval', () => {
    expect(canAttemptApprove(makeVo({ status: 'Draft' }), 'someone-else')).toBe(false)
    expect(canAttemptApprove(makeVo({ status: 'Approved' }), 'someone-else')).toBe(false)
  })

  it('true for an unrelated authenticated user while PendingApproval', () => {
    expect(canAttemptApprove(makeVo(), 'someone-else')).toBe(true)
  })

  it('false for the creator/submitter when AllowSelfApproval is false (the pinned-policy default)', () => {
    expect(canAttemptApprove(makeVo(), 'creator-1')).toBe(false)
    expect(canAttemptApprove(makeVo(), 'submitter-1')).toBe(false)
  })

  it('true for the creator/submitter when AllowSelfApproval is true — read from the real DTO field, never guessed', () => {
    expect(canAttemptApprove(makeVo({ allowSelfApproval: true }), 'creator-1')).toBe(true)
    expect(canAttemptApprove(makeVo({ allowSelfApproval: true }), 'submitter-1')).toBe(true)
  })

  it('true when there is no known current user id (never crashes on missing claims)', () => {
    expect(canAttemptApprove(makeVo(), null)).toBe(true)
  })
})

describe('canAttemptReturnForRevision', () => {
  it('true whenever PendingApproval, regardless of step position (§8.4: the deadlock escape valve)', () => {
    expect(canAttemptReturnForRevision(makeVo({ currentStepNo: 1, totalSteps: 3 }))).toBe(true)
    expect(canAttemptReturnForRevision(makeVo({ currentStepNo: 3, totalSteps: 3 }))).toBe(true)
  })

  it('false outside PendingApproval', () => {
    expect(canAttemptReturnForRevision(makeVo({ status: 'Draft' }))).toBe(false)
    expect(canAttemptReturnForRevision(makeVo({ status: 'Rejected' }))).toBe(false)
  })
})

describe('canAttemptReject', () => {
  it('false when not at the structurally-final step', () => {
    expect(canAttemptReject(makeVo({ currentStepNo: 2, totalSteps: 3 }))).toBe(false)
  })

  it('true only at the final step while PendingApproval', () => {
    expect(canAttemptReject(makeVo({ currentStepNo: 3, totalSteps: 3 }))).toBe(true)
  })

  it('false outside PendingApproval even at the final step number', () => {
    expect(canAttemptReject(makeVo({ status: 'Approved', currentStepNo: 3, totalSteps: 3 }))).toBe(false)
  })

  it('false when totalSteps is 0 (no chain attached yet)', () => {
    expect(canAttemptReject(makeVo({ currentStepNo: 0, totalSteps: 0 }))).toBe(false)
  })
})

describe('canAttemptWithdraw', () => {
  it('true for the submitter at step 1 while PendingApproval', () => {
    expect(canAttemptWithdraw(makeVo({ currentStepNo: 1 }), 'submitter-1')).toBe(true)
  })

  it('false once a step has cleared (currentStepNo > 1)', () => {
    expect(canAttemptWithdraw(makeVo({ currentStepNo: 2 }), 'submitter-1')).toBe(false)
  })

  it('false for anyone other than the submitter', () => {
    expect(canAttemptWithdraw(makeVo({ currentStepNo: 1 }), 'creator-1')).toBe(false)
    expect(canAttemptWithdraw(makeVo({ currentStepNo: 1 }), null)).toBe(false)
  })

  it('false outside PendingApproval', () => {
    expect(canAttemptWithdraw(makeVo({ status: 'Draft', currentStepNo: 1 }), 'submitter-1')).toBe(false)
  })
})

describe('canAttemptCancel', () => {
  it('true for the creator while Draft', () => {
    expect(canAttemptCancel(makeVo({ status: 'Draft' }), 'creator-1', 'QS')).toBe(true)
  })

  it('true for any PM while Draft, even if not the creator', () => {
    expect(canAttemptCancel(makeVo({ status: 'Draft' }), 'someone-else', 'PM')).toBe(true)
  })

  it('false for a non-creator, non-PM', () => {
    expect(canAttemptCancel(makeVo({ status: 'Draft' }), 'someone-else', 'QS')).toBe(false)
  })

  it('false outside Draft', () => {
    expect(canAttemptCancel(makeVo({ status: 'PendingApproval' }), 'creator-1', 'PM')).toBe(false)
  })
})

describe('resolveChainStepTone', () => {
  it('marks steps before currentStepNo as done, the current one as current, later ones as pending', () => {
    const vo = makeVo({ currentStepNo: 2, totalSteps: 3, status: 'PendingApproval' })
    expect(resolveChainStepTone(vo, 1)).toBe('done')
    expect(resolveChainStepTone(vo, 2)).toBe('current')
    expect(resolveChainStepTone(vo, 3)).toBe('pending')
  })

  it('marks every step done once Approved (VO\'s terminal-success state)', () => {
    const approved = makeVo({ status: 'Approved', currentStepNo: 3, totalSteps: 3 })
    expect([1, 2, 3].map((n) => resolveChainStepTone(approved, n))).toEqual(['done', 'done', 'done'])
  })

  it('marks the final step rejected and earlier steps done when Rejected', () => {
    const rejected = makeVo({ status: 'Rejected', currentStepNo: 3, totalSteps: 3 })
    expect([1, 2, 3].map((n) => resolveChainStepTone(rejected, n))).toEqual(['done', 'done', 'rejected'])
  })
})
