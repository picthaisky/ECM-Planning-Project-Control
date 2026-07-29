import { describe, expect, it } from 'vitest'
import {
  NORMAL_RETENTION_CAP_WARNING_PCT,
  calculateRetentionCapAmount,
  isAboveNormalRetentionCapThreshold,
} from './retentionCap'

describe('calculateRetentionCapAmount', () => {
  it('returns null (uncapped) when retentionCapPercentage is null — Thai-standard contracts', () => {
    expect(calculateRetentionCapAmount(485_000_000, null)).toBeNull()
  })

  it('computes R^max = (c/100) * C — FIDIC fixture P3 (payment-retention.md)', () => {
    expect(calculateRetentionCapAmount(100_000_000, 5)).toBe(5_000_000)
  })

  it('never reproduces the prototype R-02 bug: a 5% rate example paired with a 10% cap is still just 10% of ContractValue, computed independently of any rate field', () => {
    // The whole point: this function takes ContractValue + RetentionCapPercentage only — it has
    // no RetentionRate parameter at all, so it cannot silently re-couple the two the way the
    // prototype's static "5% (เพดาน 10% ของสัญญา)" text implied.
    expect(calculateRetentionCapAmount(485_000_000, 10)).toBe(48_500_000)
  })
})

describe('isAboveNormalRetentionCapThreshold', () => {
  it('is false when uncapped', () => {
    expect(isAboveNormalRetentionCapThreshold(null)).toBe(false)
  })

  it('is false at or below the normal 10% reference', () => {
    expect(isAboveNormalRetentionCapThreshold(5)).toBe(false)
    expect(isAboveNormalRetentionCapThreshold(NORMAL_RETENTION_CAP_WARNING_PCT)).toBe(false)
  })

  it('is true above the normal 10% reference — non-blocking warning trigger only', () => {
    expect(isAboveNormalRetentionCapThreshold(10.01)).toBe(true)
    expect(isAboveNormalRetentionCapThreshold(25)).toBe(true)
  })
})
