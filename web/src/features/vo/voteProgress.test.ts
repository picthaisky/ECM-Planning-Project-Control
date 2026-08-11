import { describe, expect, it } from 'vitest'
import { computeStepVoteProgress } from './voteProgress'
import type { ApprovalActionDto, VariationOrderDto } from './types'

function makeVo(overrides: Partial<VariationOrderDto> = {}): VariationOrderDto {
  return {
    id: 'vo-1',
    projectId: 'project-1',
    voNumber: 'VO-1000',
    description: null,
    justification: null,
    amount: '1000000.00',
    type: 'Add',
    timeImpactDays: 0,
    status: 'PendingApproval',
    revisionNo: 1,
    currentStepNo: 1,
    totalSteps: 1,
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    allowSelfApproval: false,
    approvalSteps: [{ stepNo: 1, requiredRole: 'ProjectDirector', quorumCount: 2 }],
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

function makeAction(overrides: Partial<ApprovalActionDto> = {}): ApprovalActionDto {
  return {
    id: 'action-1',
    documentType: 'VariationOrder',
    documentId: 'vo-1',
    revisionNo: 1,
    stepNo: 1,
    actorUserId: 'pd-a',
    actorRoleAtTime: 'ProjectDirector',
    action: 'Approve',
    comment: null,
    actedAt: '2026-08-10T09:00:00+07:00',
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    ...overrides,
  }
}

describe('computeStepVoteProgress', () => {
  it('null when history is unavailable — never a guess', () => {
    expect(computeStepVoteProgress(makeVo(), null, 'Approve')).toBeNull()
  })

  it('null when no chain is attached (totalSteps 0)', () => {
    expect(computeStepVoteProgress(makeVo({ currentStepNo: 0, totalSteps: 0 }), [], 'Approve')).toBeNull()
  })

  it('null when the current step has no matching snapshot row (corrupt/legacy chain)', () => {
    expect(computeStepVoteProgress(makeVo({ currentStepNo: 5 }), [], 'Approve')).toBeNull()
  })

  it('counts distinct approvers on the current revision+step, ignoring other steps/revisions/actions', () => {
    const vo = makeVo()
    const history: ApprovalActionDto[] = [
      makeAction({ actorUserId: 'pd-a', action: 'Approve' }),
      makeAction({ actorUserId: 'pd-a', action: 'Approve' }), // duplicate actor — still counts once
      makeAction({ actorUserId: 'pd-b', action: 'Reject' }), // wrong action — excluded
      makeAction({ actorUserId: 'pd-c', action: 'Approve', stepNo: 2 }), // wrong step — excluded
      makeAction({ actorUserId: 'pd-d', action: 'Approve', revisionNo: 2 }), // wrong revision — excluded
    ]

    expect(computeStepVoteProgress(vo, history, 'Approve')).toEqual({ required: 2, satisfied: 1 })
  })

  it('V-11b (domain-rules.md §8.5): after the first of two required rejectors, satisfied=1 of 2', () => {
    const vo = makeVo()
    const history: ApprovalActionDto[] = [makeAction({ actorUserId: 'pd-a', action: 'Reject' })]

    expect(computeStepVoteProgress(vo, history, 'Reject')).toEqual({ required: 2, satisfied: 1 })
  })

  it('reaches required=satisfied once enough distinct voters have acted', () => {
    const vo = makeVo()
    const history: ApprovalActionDto[] = [
      makeAction({ actorUserId: 'pd-a', action: 'Approve' }),
      makeAction({ actorUserId: 'pd-b', action: 'Approve' }),
    ]

    expect(computeStepVoteProgress(vo, history, 'Approve')).toEqual({ required: 2, satisfied: 2 })
  })
})
