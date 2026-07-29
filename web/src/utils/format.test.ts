import { describe, expect, it } from 'vitest'
import { formatMoney, formatPercent, roundHalfAwayFromZero } from './format'

describe('roundHalfAwayFromZero', () => {
  it('rounds the classic banker-rounding trap fixture (payment-retention.md P7) up, not down', () => {
    // 50,000.005 must round to 50,000.01 (half-away-from-zero) — banker's rounding wrongly
    // yields 50,000.00 for this exact midpoint, which is the regression this fixture catches.
    expect(roundHalfAwayFromZero(50_000.005, 2)).toBeCloseTo(50_000.01, 5)
  })

  it('rounds negative values away from zero, not toward it', () => {
    expect(roundHalfAwayFromZero(-2.5, 0)).toBe(-3)
  })

  it('leaves already-precise values unchanged', () => {
    expect(roundHalfAwayFromZero(18_360_000, 2)).toBe(18_360_000)
  })
})

describe('formatMoney', () => {
  it('formats with thousand separators and exactly 2 decimals', () => {
    expect(formatMoney(485_000_000)).toBe('485,000,000.00')
  })

  it('accepts a decimal-safe string (the API wire shape)', () => {
    expect(formatMoney('18360000.5')).toBe('18,360,000.50')
  })

  it('falls back to an em dash for a non-numeric input rather than throwing or showing NaN', () => {
    expect(formatMoney('not-a-number')).toBe('—')
  })
})

describe('formatPercent', () => {
  it('formats with exactly 2 decimals by default (decimal(5,2) rate fields)', () => {
    expect(formatPercent(5)).toBe('5.00%')
    expect(formatPercent(12.345)).toBe('12.35%')
  })

  it('supports a custom decimal count (e.g. the dashboard 1-decimal cadence)', () => {
    expect(formatPercent(54.2, 1)).toBe('54.2%')
  })
})
