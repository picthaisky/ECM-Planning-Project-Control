import { describe, expect, it } from 'vitest'
import { computeBacImpact } from './bacImpact'
import type { BacImpactInput } from './bacImpact'

/** `docs/specs/variation-order/domain-rules.md` §5.7 fixture V-9's BAC move: an `Add +10,000,000.00`
 * VO steps `BAC` 100,000,000.00 → 110,000,000.00. V-9 itself is about the five EAC variants across
 * the rebaseline boundary (backend-owned, `RebaselineTests.cs`) — this test covers only the one
 * slice this module owns: the signed BAC/ContractValue move itself. */
const V9_BASE: BacImpactInput = {
  bacBefore: 100_000_000,
  contractValueBefore: 100_000_000,
  amount: 10_000_000,
  cumulativeApprovedVoBefore: null,
  escalationBaselineContractValue: null,
  escalationThresholdPct: null,
}

describe('computeBacImpact — BAC/ContractValue move (domain-rules.md §5.1, fixture V-9)', () => {
  it('moves BAC and ContractValue by the signed amount — an Add VO raises both', () => {
    const result = computeBacImpact(V9_BASE)

    expect(result.bacBefore).toBe(100_000_000)
    expect(result.bacAfter).toBe(110_000_000)
    expect(result.bacDelta).toBe(10_000_000)
    expect(result.contractValueAfter).toBe(110_000_000)
    expect(result.contractValueDelta).toBe(10_000_000)
  })

  it('a Deduct VO LOWERS both — never renders abs(), the sign is preserved throughout', () => {
    // R2 (domain-rules.md §3.4): VO-015 `Deduct -800,000.00`.
    const result = computeBacImpact({
      ...V9_BASE,
      bacBefore: 485_000_000,
      contractValueBefore: 485_000_000,
      amount: -800_000,
    })

    expect(result.bacAfter).toBe(484_200_000)
    expect(result.bacDelta).toBe(-800_000)
    expect(result.contractValueAfter).toBe(484_200_000)
    expect(result.contractValueDelta).toBe(-800_000)
  })

  it('amount = 0 (a time-only variation, domain-rules.md §7.3) moves neither figure', () => {
    const result = computeBacImpact({ ...V9_BASE, amount: 0 })
    expect(result.bacAfter).toBe(result.bacBefore)
    expect(result.contractValueAfter).toBe(result.contractValueBefore)
  })
})

const ESCALATION_BASE: BacImpactInput = {
  bacBefore: 485_000_000,
  contractValueBefore: 485_000_000,
  amount: 3_200_000,
  cumulativeApprovedVoBefore: null,
  escalationBaselineContractValue: 485_000_000,
  escalationThresholdPct: 10,
}

describe('computeBacImpact — escalation assessment (domain-rules.md §4, ADR-0015)', () => {
  it('V-5a: exactly at the threshold (10.000000%) does NOT escalate — strict ">"', () => {
    const result = computeBacImpact({
      ...ESCALATION_BASE,
      cumulativeApprovedVoBefore: 45_300_000,
    })

    expect(result.escalation.status).toBe('below-threshold')
    if (result.escalation.status === 'below-threshold') {
      expect(result.escalation.cumulativeAmount).toBe(48_500_000)
      expect(result.escalation.pct).toBeCloseTo(10, 10)
    }
  })

  it('V-5b: 10.004000% (unrounded) DOES escalate — rounding must never decide this', () => {
    // The fixture exists specifically to catch an implementation that rounds Phi to decimal(5,2)
    // before comparing: 10.004000% displays as "10.00%" once rounded, which would wrongly read as
    // "not greater than 10.00" if the comparison ran on the rounded value instead of the raw one.
    const result = computeBacImpact({
      ...ESCALATION_BASE,
      cumulativeApprovedVoBefore: 45_319_400,
    })

    expect(result.escalation.status).toBe('crosses-threshold')
    if (result.escalation.status === 'crosses-threshold') {
      expect(result.escalation.cumulativeAmount).toBe(48_519_400)
      expect(result.escalation.pct).toBeCloseTo(10.004, 10)
      expect(result.escalation.thresholdPct).toBe(10)
    }
  })

  it('threshold = null means "not configured" — never rendered/treated as a 0% threshold', () => {
    const result = computeBacImpact({
      ...ESCALATION_BASE,
      cumulativeApprovedVoBefore: 999_000_000, // would escalate against any real threshold
      escalationThresholdPct: null,
    })

    expect(result.escalation).toEqual({ status: 'not-configured' })
  })

  it('missing prior-VO total or baseline (both realistically unknown to the FE today) reports "unknown", not a guess', () => {
    expect(computeBacImpact({ ...ESCALATION_BASE, cumulativeApprovedVoBefore: null }).escalation.status).toBe(
      'unknown',
    )
    expect(
      computeBacImpact({
        ...ESCALATION_BASE,
        cumulativeApprovedVoBefore: 45_300_000,
        escalationBaselineContractValue: null,
      }).escalation.status,
    ).toBe('unknown')
    expect(
      computeBacImpact({
        ...ESCALATION_BASE,
        cumulativeApprovedVoBefore: 45_300_000,
        escalationBaselineContractValue: 0,
      }).escalation.status,
    ).toBe('unknown')
  })

  it('carries the configured escalation role through for display, on both outcomes', () => {
    const below = computeBacImpact({
      ...ESCALATION_BASE,
      cumulativeApprovedVoBefore: 45_300_000,
      escalationRole: 'Executive',
    })
    const above = computeBacImpact({
      ...ESCALATION_BASE,
      cumulativeApprovedVoBefore: 45_319_400,
      escalationRole: 'Executive',
    })

    expect(below.escalation.status === 'below-threshold' && below.escalation.role).toBe('Executive')
    expect(above.escalation.status === 'crosses-threshold' && above.escalation.role).toBe('Executive')
  })
})
