import { describe, expect, it } from 'vitest'
import { computeBarValueDomain, computeGroupLayout, scaleBarValue, zeroBaselineY } from './cashFlowBarScale'

describe('computeBarValueDomain', () => {
  it('always includes 0 as the minimum for all-positive values, plus 8% headroom above the max', () => {
    const domain = computeBarValueDomain([100, 50, 80])
    expect(domain.minValue).toBe(0)
    expect(domain.maxValue).toBeCloseTo(108, 5)
  })

  it('extends below zero for a genuinely negative value (actual-cost.md §7.6 over-reversal edge case), never clamped', () => {
    const domain = computeBarValueDomain([100, -20])
    expect(domain.minValue).toBe(-20)
    expect(domain.maxValue).toBeCloseTo(100 + 120 * 0.08, 5)
  })

  it('degenerate: an empty array is still a well-formed domain, no divide-by-zero downstream', () => {
    expect(computeBarValueDomain([])).toEqual({ minValue: 0, maxValue: 1 })
  })

  it('degenerate: every value exactly 0 (task note #3 — a genuinely sparse chart) is still well-formed', () => {
    expect(computeBarValueDomain([0, 0, 0])).toEqual({ minValue: 0, maxValue: 1 })
  })
})

describe('scaleBarValue / zeroBaselineY', () => {
  const domain = { minValue: 0, maxValue: 100 }

  it('projects the max value to y=0 (top) and the min value to y=innerHeight (bottom) — inverted, SVG y grows downward', () => {
    expect(scaleBarValue(100, domain, 200)).toBeCloseTo(0, 5)
    expect(scaleBarValue(0, domain, 200)).toBeCloseTo(200, 5)
  })

  it('zeroBaselineY matches scaleBarValue(0, ...) exactly', () => {
    expect(zeroBaselineY(domain, 200)).toBe(scaleBarValue(0, domain, 200))
  })

  it('a negative-inclusive domain places the zero baseline above the bottom edge, not at it', () => {
    const negDomain = { minValue: -20, maxValue: 108 }
    const y = zeroBaselineY(negDomain, 128) // span = 128, 0 sits 20/128 of the way up from the bottom
    expect(y).toBeCloseTo(128 - (20 / 128) * 128, 5)
    expect(y).toBeLessThan(128)
    expect(y).toBeGreaterThan(0)
  })
})

describe('computeGroupLayout', () => {
  it('splits innerWidth evenly across groupCount clusters, each holding exactly 3 equal-width bars', () => {
    const layout = computeGroupLayout(0, 2, 400)
    expect(layout.barWidth * 3).toBeCloseTo(layout.groupWidth, 5)
    expect(layout.groupWidth).toBeLessThan(200) // less than the full 200px slot, since a gap is reserved
  })

  it('a single period (task note #3 — "essentially one bar") still produces one well-formed, non-degenerate cluster', () => {
    const layout = computeGroupLayout(0, 1, 400)
    expect(layout.groupWidth).toBeGreaterThan(0)
    expect(layout.barWidth).toBeGreaterThan(0)
    expect(layout.groupX).toBeGreaterThanOrEqual(0)
  })

  it('later indices are placed further right, clusters never overlap', () => {
    const first = computeGroupLayout(0, 3, 600)
    const second = computeGroupLayout(1, 3, 600)
    expect(second.groupX).toBeGreaterThan(first.groupX + first.groupWidth)
  })
})
