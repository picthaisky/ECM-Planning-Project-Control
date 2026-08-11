import { describe, expect, it } from 'vitest'
import { formatMoney, formatMoneyMillions, formatPercent, formatRatio, roundHalfAwayFromZero } from './format'

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

describe('formatRatio', () => {
  it('formats with exactly 2 decimals by default and no thousand separator/unit', () => {
    expect(formatRatio(0.92)).toBe('0.92')
    expect(formatRatio(1.04)).toBe('1.04')
  })

  it('accepts a decimal-safe string (the API wire shape — RoundingRules.RatioDecimals = 6dp)', () => {
    expect(formatRatio('0.857143')).toBe('0.86')
    expect(formatRatio('1.166667')).toBe('1.17')
  })

  it('falls back to an em dash for a non-numeric input rather than throwing or showing NaN', () => {
    expect(formatRatio('not-a-number')).toBe('—')
  })

  it('supports a custom decimal count', () => {
    expect(formatRatio('1.166667', 4)).toBe('1.1667')
  })
})

describe('formatMoneyMillions', () => {
  it('scales baht to million-baht with exactly 2 decimals and the "MB" suffix (prototype convention)', () => {
    expect(formatMoneyMillions(466_000_000)).toBe('466.00 MB')
    expect(formatMoneyMillions(238_400_000)).toBe('238.40 MB')
  })

  it('accepts a decimal-safe string (the API wire shape)', () => {
    expect(formatMoneyMillions('253100000.00')).toBe('253.10 MB')
  })

  it('keeps the sign for a negative funding position (Net Cash Position)', () => {
    expect(formatMoneyMillions(-14_700_000)).toBe('-14.70 MB')
  })

  it('rounds to 2dp (not truncated) — half-away-from-zero midpoint behavior itself is covered by roundHalfAwayFromZero above', () => {
    expect(formatMoneyMillions(1_236_000)).toBe('1.24 MB') // 1.236 MB -> 1.24, proves rounding not truncation
  })

  it('falls back to an em dash for a non-numeric input rather than throwing or showing NaN', () => {
    expect(formatMoneyMillions('not-a-number')).toBe('—')
  })
})
