import { describe, expect, it } from 'vitest'
import { buildEscalationNote } from './escalationNote'
import type { VariationOrderDto } from './types'

function makeVo(overrides: Partial<VariationOrderDto> = {}): VariationOrderDto {
  return {
    id: 'vo-1',
    projectId: 'project-1',
    voNumber: 'VO-030',
    description: null,
    justification: null,
    amount: '3200000.00',
    type: 'Add',
    timeImpactDays: 0,
    status: 'PendingApproval',
    revisionNo: 1,
    currentStepNo: 1,
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
    submittedByUserId: 'creator-1',
    submittedAt: '2026-08-10T00:00:00+07:00',
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

describe('buildEscalationNote', () => {
  it('null when there is no chain at all', () => {
    expect(buildEscalationNote(makeVo({ totalSteps: 0, approvalSteps: [] }), 6_000_000)).toBeNull()
  })

  it('null when the final role is not Executive (ordinary routing, nothing to explain)', () => {
    const vo = makeVo({
      totalSteps: 2,
      approvalSteps: [
        { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
        { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
      ],
    })
    expect(buildEscalationNote(vo, 6_000_000)).toBeNull()
  })

  it('R4 fixture (domain-rules.md §3.4): pending, cumulative context known — hedged wording, no false certainty', () => {
    const note = buildEscalationNote(makeVo(), 46_000_000)
    expect(note).toContain('Executive')
    expect(note).toContain('อาจเกี่ยวข้องกับ')
    expect(note).toContain('49,200,000.00')
  })

  it('pending, no cumulative context known — states only the bare structural fact', () => {
    const note = buildEscalationNote(makeVo(), null)
    expect(note).toBe('ขั้นตอนอนุมัติสุดท้ายกำหนดให้ Executive อนุมัติ')
  })

  it('Approved with a real recorded percentage — stated as fact, not hedged', () => {
    const vo = makeVo({ status: 'Approved', currentStepNo: 3, cumulativeVoPctAtApproval: '10.14' })
    const note = buildEscalationNote(vo, 46_000_000)
    expect(note).toContain('10.14%')
    expect(note).not.toContain('อาจเกี่ยวข้องกับ')
  })
})
