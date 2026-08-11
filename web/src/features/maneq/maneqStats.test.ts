import { describe, expect, it } from 'vitest'
import { isImplausiblePi, manningVarianceBand, parseManpowerDecimal, piBand } from './maneqStats'

describe('parseManpowerDecimal', () => {
  it('parses a decimal-as-string field to a number', () => {
    expect(parseManpowerDecimal('180.00')).toBe(180)
    expect(parseManpowerDecimal('0.60')).toBe(0.6)
  })

  it('returns null for a JSON null (never coerces to 0)', () => {
    expect(parseManpowerDecimal(null)).toBeNull()
  })

  it('returns null for a non-numeric string rather than NaN', () => {
    expect(parseManpowerDecimal('not-a-number')).toBeNull()
  })
})

describe('piBand — domain-rules.md §5.3 (higher is better, 1.00 is on budget)', () => {
  it('fixture M-01: 0.90 bands as gold ("ต่ำกว่าแผนเล็กน้อย")', () => {
    expect(piBand(0.9)).toBe('gold')
  })

  it('bands >= 0.95 as success, including exactly on budget (1.00) and better-than-budget', () => {
    expect(piBand(0.95)).toBe('success')
    expect(piBand(1.0)).toBe('success')
    expect(piBand(1.3)).toBe('success')
  })

  it('bands the [0.85, 0.95) range as gold', () => {
    expect(piBand(0.85)).toBe('gold')
    expect(piBand(0.94)).toBe('gold')
  })

  it('bands below 0.85 as danger, including a real defined 0.00 (fixture M-06c) and negative (M-06h)', () => {
    expect(piBand(0.84)).toBe('danger')
    expect(piBand(0)).toBe('danger')
    expect(piBand(-0.6)).toBe('danger')
  })

  it('bands null as its own "null" band, never coerced into a numeric band', () => {
    expect(piBand(null)).toBe('null')
  })
})

describe('manningVarianceBand — §9.2/§5.3, the neutral (never green) palette', () => {
  it('fixture M-02: manningRatio 1.25 (actual 25 vs planned 20) is "above" — never treated as good news', () => {
    expect(manningVarianceBand(25, 20)).toBe('above')
  })

  it('is within ±5% of plan -> onplan', () => {
    expect(manningVarianceBand(205, 200)).toBe('onplan') // +2.5%
    expect(manningVarianceBand(190, 200)).toBe('onplan') // -5%
  })

  it('more than 5% below plan -> below', () => {
    expect(manningVarianceBand(186, 205)).toBe('below') // -9.27%
  })

  it('more than 5% above plan -> above, not "onplan" and never treated as success', () => {
    expect(manningVarianceBand(230, 200)).toBe('above') // +15%
  })

  it('no plan configured (null) -> noplan, not a fabricated below/above reading', () => {
    expect(manningVarianceBand(186, null)).toBe('noplan')
  })

  it('an explicit plan of 0 is treated as no usable plan for a ratio band (guards divide-by-zero)', () => {
    expect(manningVarianceBand(10, 0)).toBe('noplan')
  })

  it('reproduces the exact prototype defect scenario correctly: 30 over plan is not "onplan" nor green', () => {
    // ECM Planning Prototype.dc.html:872's `d >= -10 ? green : red` would paint +30 green.
    expect(manningVarianceBand(230, 200)).not.toBe('onplan')
  })
})

describe('isImplausiblePi', () => {
  it('flags values outside [0.20, 3.00]', () => {
    expect(isImplausiblePi(0.1)).toBe(true)
    expect(isImplausiblePi(3.5)).toBe(true)
  })

  it('does not flag values inside the range, including the boundaries', () => {
    expect(isImplausiblePi(0.2)).toBe(false)
    expect(isImplausiblePi(3.0)).toBe(false)
    expect(isImplausiblePi(0.9)).toBe(false)
  })

  it('does not flag null', () => {
    expect(isImplausiblePi(null)).toBe(false)
  })
})
