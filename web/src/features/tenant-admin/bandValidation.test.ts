import { describe, expect, it } from 'vitest'
import {
  MAX_CLIENT_QUORUM_COUNT,
  validateApprovalPolicyBands,
  validatePolicyDraft,
  validateRuleFields,
} from './bandValidation'
import type { ApprovalPolicyRule } from './types'

function rule(overrides: Partial<ApprovalPolicyRule> = {}): ApprovalPolicyRule {
  return { stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'PM', quorumCount: 1, ...overrides }
}

describe('validateApprovalPolicyBands', () => {
  it('TH-Default-VO (approval-workflow.md §8): a genuine 3-tier DoA has no band problems', () => {
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: 500_000 },
      { stepNo: 1, minAmount: 500_000, maxAmount: 5_000_000 },
      { stepNo: 2, minAmount: 500_000, maxAmount: 5_000_000 },
      { stepNo: 1, minAmount: 5_000_000, maxAmount: null },
      { stepNo: 2, minAmount: 5_000_000, maxAmount: null },
      { stepNo: 3, minAmount: 5_000_000, maxAmount: null },
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([])
  })

  it('TH-Default-IPC (approval-workflow.md §8): QS/PM/ProjectDirector tiered by amount has no band problems', () => {
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: null }, // QS
      { stepNo: 2, minAmount: 0, maxAmount: null }, // PM
      { stepNo: 3, minAmount: 10_000_000, maxAmount: null }, // ProjectDirector
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([])
  })

  it('the H-01 attack shape (two different roles sharing StepNo 1 over disjoint bands) is legitimate, not an overlap', () => {
    // security review sprint-09.md §2 H-01: StepNo1=QS[0,1M), StepNo1=PM[1M,inf), StepNo2=PD[1M,inf).
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: 1_000_000 },
      { stepNo: 1, minAmount: 1_000_000, maxAmount: null },
      { stepNo: 2, minAmount: 1_000_000, maxAmount: null },
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([])
  })

  it('the M-03 attack shape (two rules genuinely claiming the same StepNo over the same range) is an overlap', () => {
    // security review sprint-09.md §3 M-03: ApprovalPolicySeeder-style duplicate StepNo 1.
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: null },
      { stepNo: 1, minAmount: 0, maxAmount: null },
      { stepNo: 2, minAmount: 0, maxAmount: null },
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([{ kind: 'overlap', stepNo: 1 }])
  })

  it('detects a partial overlap (not merely an identical duplicate range)', () => {
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: null }, // covers the whole axis, so only the StepNo 2 problem below should surface
      { stepNo: 2, minAmount: 0, maxAmount: 600_000 },
      { stepNo: 2, minAmount: 500_000, maxAmount: 900_000 }, // overlaps [500,000, 600,000)
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([{ kind: 'overlap', stepNo: 2 }])
  })

  it('touching (not overlapping) boundaries are fine — half-open interval semantics', () => {
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: 500_000 },
      { stepNo: 1, minAmount: 500_000, maxAmount: null },
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([])
  })

  it('detects a missing middle StepNo (a genuine gap) in the interval where it matters', () => {
    const bands = [
      { stepNo: 1, minAmount: 0, maxAmount: null }, // covers everything
      { stepNo: 3, minAmount: 1_000_000, maxAmount: null }, // no StepNo 2 anywhere
    ]
    expect(validateApprovalPolicyBands(bands)).toEqual([{ kind: 'gap', stepNo: 2 }])
  })

  it('an amount range nobody claims at all is legal (sparse policy, approval-workflow.md §8 fixture R5 shape)', () => {
    const bands = [{ stepNo: 1, minAmount: 100_000, maxAmount: null }]
    expect(validateApprovalPolicyBands(bands)).toEqual([])
  })
})

describe('validateRuleFields', () => {
  it('no issues for a well-formed rule set', () => {
    expect(validateRuleFields([rule(), rule({ stepNo: 2, minAmount: '500000.00' })])).toEqual([])
  })

  it('flags StepNo < 1, negative MinAmount, MaxAmount <= MinAmount, and missing role', () => {
    const issues = validateRuleFields([
      rule({ stepNo: 0 }),
      rule({ minAmount: '-1.00' }),
      rule({ minAmount: '100.00', maxAmount: '50.00' }),
      rule({ requiredRole: undefined as unknown as ApprovalPolicyRule['requiredRole'] }),
    ])
    expect(issues).toHaveLength(4)
    expect(issues[0]).toMatchObject({ ruleIndex: 0, stepNo: expect.any(String) })
    expect(issues[1]).toMatchObject({ ruleIndex: 1, minAmount: expect.any(String) })
    expect(issues[2]).toMatchObject({ ruleIndex: 2, maxAmount: expect.any(String) })
    expect(issues[3]).toMatchObject({ ruleIndex: 3, requiredRole: expect.any(String) })
  })

  it('accepts a blank maxAmount as "unbounded", never a validation error', () => {
    expect(validateRuleFields([rule({ maxAmount: '' })])).toEqual([])
    expect(validateRuleFields([rule({ maxAmount: null })])).toEqual([])
  })

  describe('QuorumCount', () => {
    it(`is a foot-gun guard, not server enforcement — rejects anything above ${MAX_CLIENT_QUORUM_COUNT} in this UI`, () => {
      const issues = validateRuleFields([rule({ quorumCount: MAX_CLIENT_QUORUM_COUNT + 1 })])
      expect(issues).toHaveLength(1)
      expect(issues[0].quorumCount).toContain(String(MAX_CLIENT_QUORUM_COUNT))
    })

    it(`accepts exactly ${MAX_CLIENT_QUORUM_COUNT} (inclusive bound)`, () => {
      expect(validateRuleFields([rule({ quorumCount: MAX_CLIENT_QUORUM_COUNT })])).toEqual([])
    })

    it('rejects QuorumCount below 1', () => {
      const issues = validateRuleFields([rule({ quorumCount: 0 })])
      expect(issues).toHaveLength(1)
      expect(issues[0].quorumCount).toBeDefined()
    })
  })
})

describe('validatePolicyDraft', () => {
  it('valid: a well-formed multi-rule draft has no issues at all', () => {
    const result = validatePolicyDraft([
      rule({ stepNo: 1, minAmount: '0.00', maxAmount: '500000.00' }),
      rule({ stepNo: 1, minAmount: '500000.00', maxAmount: null, requiredRole: 'ProjectDirector' }),
    ])
    expect(result).toEqual({ fieldIssues: [], bandProblems: [], hasNoRules: false, isValid: true })
  })

  it('invalid: an empty rule set is reported distinctly (hasNoRules), mirroring the NotEmpty() validator', () => {
    const result = validatePolicyDraft([])
    expect(result.hasNoRules).toBe(true)
    expect(result.isValid).toBe(false)
  })

  it('does not attempt band validation while a field is malformed (avoids NaN-derived noise)', () => {
    const result = validatePolicyDraft([rule({ minAmount: 'not-a-number' })])
    expect(result.fieldIssues).toHaveLength(1)
    expect(result.bandProblems).toEqual([])
    expect(result.isValid).toBe(false)
  })

  it('invalid: band overlap is surfaced once fields are individually well-formed', () => {
    const result = validatePolicyDraft([rule({ stepNo: 1 }), rule({ stepNo: 1, requiredRole: 'QS' })])
    expect(result.fieldIssues).toEqual([])
    expect(result.bandProblems).toEqual([{ kind: 'overlap', stepNo: 1 }])
    expect(result.isValid).toBe(false)
  })
})
