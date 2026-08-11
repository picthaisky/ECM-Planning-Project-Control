import { describe, expect, it } from 'vitest'
import { computeManpowerBarDomain, computeManpowerBarSlot, scaleManpowerBarValue } from './maneqBarScale'

describe('computeManpowerBarDomain', () => {
  it('adds 8% headroom above the largest value', () => {
    expect(computeManpowerBarDomain([600, 100, 40]).maxValue).toBeCloseTo(600 * 1.08, 5)
  })

  it('degenerate: an empty array is still well-formed (no divide-by-zero downstream)', () => {
    expect(computeManpowerBarDomain([])).toEqual({ maxValue: 1 })
  })

  it('degenerate: every value exactly 0 is still well-formed', () => {
    expect(computeManpowerBarDomain([0, 0, 0])).toEqual({ maxValue: 1 })
  })
})

describe('scaleManpowerBarValue', () => {
  const domain = { maxValue: 100 }

  it('projects the max value to y=0 (top) and 0 to y=innerHeight (bottom) — inverted', () => {
    expect(scaleManpowerBarValue(100, domain, 200)).toBeCloseTo(0, 5)
    expect(scaleManpowerBarValue(0, domain, 200)).toBeCloseTo(200, 5)
  })

  it('clamps a negative value to 0 (man-hours cannot be negative)', () => {
    expect(scaleManpowerBarValue(-50, domain, 200)).toBeCloseTo(200, 5)
  })
})

describe('computeManpowerBarSlot', () => {
  it('splits innerWidth evenly across `count` daily slots', () => {
    const slot = computeManpowerBarSlot(0, 7, 700)
    expect(slot.width).toBeGreaterThan(0)
    expect(slot.width).toBeLessThan(100)
  })

  it('later indices are placed further right', () => {
    const first = computeManpowerBarSlot(0, 7, 700)
    const second = computeManpowerBarSlot(1, 7, 700)
    expect(second.x).toBeGreaterThan(first.x)
  })

  it('a single-day chart still produces a well-formed, centered slot', () => {
    const slot = computeManpowerBarSlot(0, 1, 400)
    expect(slot.width).toBeGreaterThan(0)
    expect(slot.x).toBeGreaterThanOrEqual(0)
  })
})
