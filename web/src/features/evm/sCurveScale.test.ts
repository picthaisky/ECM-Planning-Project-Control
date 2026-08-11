import { describe, expect, it } from 'vitest'
import {
  FORECAST_TIME_EXTENSION_RATIO,
  buildPolylinePath,
  computeTimeDomain,
  computeValueDomain,
  scaleTime,
  scaleValue,
} from './sCurveScale'

describe('computeTimeDomain', () => {
  it('spans min to max of the given times', () => {
    const domain = computeTimeDomain([100, 300, 200])
    expect(domain.minTime).toBe(100)
    expect(domain.maxTime).toBe(300)
  })

  it('extends the forecast time by FORECAST_TIME_EXTENSION_RATIO of the real span', () => {
    // A realistic multi-month span (real EVM data dates are always many days apart) so the
    // one-day floor below doesn't also kick in and confuse what this test is asserting.
    const ninetyDaysMs = 90 * 24 * 60 * 60 * 1000
    const domain = computeTimeDomain([0, ninetyDaysMs])
    expect(domain.forecastTime).toBe(ninetyDaysMs + ninetyDaysMs * FORECAST_TIME_EXTENSION_RATIO)
  })

  it('floors the span at one day, so a sub-day input range never produces a barely-visible forecast tail', () => {
    const domain = computeTimeDomain([0, 1000]) // 1 second apart — far less than a day
    const oneDayMs = 24 * 60 * 60 * 1000
    expect(domain.forecastTime).toBe(1000 + oneDayMs * FORECAST_TIME_EXTENSION_RATIO)
  })

  it('never produces a zero-width span for a single point (guards downstream division)', () => {
    const domain = computeTimeDomain([500])
    expect(domain.minTime).toBe(500)
    expect(domain.maxTime).toBe(500)
    expect(domain.forecastTime).toBeGreaterThan(domain.maxTime)
  })

  it('handles an empty array without throwing (defensive — should not occur once evm has loaded)', () => {
    expect(() => computeTimeDomain([])).not.toThrow()
    const domain = computeTimeDomain([])
    expect(domain.forecastTime).toBeGreaterThan(domain.minTime)
  })
})

describe('computeValueDomain', () => {
  it('always starts at 0 and covers the largest of pv/ev/ac/bac with 8% headroom', () => {
    const domain = computeValueDomain([{ pv: 400_000, ev: 300_000, ac: 350_000 }], 1_000_000, null)
    expect(domain.minValue).toBe(0)
    expect(domain.maxValue).toBeCloseTo(1_000_000 * 1.08, 5)
  })

  it('stretches further to fit a computable forecast EAC above BAC', () => {
    const domain = computeValueDomain([{ pv: 400_000, ev: 300_000, ac: 350_000 }], 1_000_000, 1_438_888.89)
    expect(domain.maxValue).toBeCloseTo(1_438_888.89 * 1.08, 2)
  })

  it('ignores a null forecast EAC (nothing to fit) rather than crashing', () => {
    const domain = computeValueDomain([{ pv: 100, ev: 100, ac: 100 }], 1_000, null)
    expect(domain.maxValue).toBeCloseTo(1_000 * 1.08, 5)
  })

  it('never produces a zero-height domain when every value is 0 (a brand-new, not-started project)', () => {
    const domain = computeValueDomain([{ pv: 0, ev: 0, ac: 0 }], 0, null)
    expect(domain.maxValue).toBe(1)
  })
})

describe('scaleTime / scaleValue', () => {
  const timeDomain = { minTime: 0, maxTime: 1000, forecastTime: 1250 }
  const valueDomain = { minValue: 0, maxValue: 1000 }

  it('maps the domain start to 0 and the forecast time to innerWidth', () => {
    expect(scaleTime(0, timeDomain, 500)).toBe(0)
    expect(scaleTime(1250, timeDomain, 500)).toBe(500)
  })

  it('maps a value of 0 to innerHeight (bottom) and maxValue to 0 (top) — SVG y grows downward', () => {
    expect(scaleValue(0, valueDomain, 300)).toBe(300)
    expect(scaleValue(1000, valueDomain, 300)).toBe(0)
  })

  it('maps the domain midpoint to the middle of the pixel range for both axes', () => {
    expect(scaleTime(625, timeDomain, 500)).toBeCloseTo(250, 5)
    expect(scaleValue(500, valueDomain, 300)).toBeCloseTo(150, 5)
  })
})

describe('buildPolylinePath', () => {
  it('builds an M/L path connecting points with straight segments (never a curve fit)', () => {
    const path = buildPolylinePath([
      { x: 0, y: 10 },
      { x: 5, y: 20 },
      { x: 10, y: 0 },
    ])
    expect(path).toBe('M0.00,10.00 L5.00,20.00 L10.00,0.00')
    expect(path).not.toContain('C') // no cubic-bezier command anywhere
  })

  it('returns an empty string for no points', () => {
    expect(buildPolylinePath([])).toBe('')
  })

  it('handles a single point (produces a lone moveto, renders as nothing visible but never throws)', () => {
    expect(buildPolylinePath([{ x: 1, y: 2 }])).toBe('M1.00,2.00')
  })
})
