/**
 * Pure geometry/scale helpers for `components/ManpowerHistogramChart.tsx` — mirrors
 * `features/cash/cashFlowBarScale.ts`'s split between pure coordinate math (this file) and the
 * component that renders it, so the geometry is unit-testable without mounting React/SVG. One bar
 * per day (not a 3-series group like Cash Flow's PV/EV/AC) since the histogram plots a single
 * man-hours total per day (domain-rules.md §9.1: "Y = man-hours, not headcount").
 */

export interface BarValueDomain {
  maxValue: number
}

/** Value (y) domain always starts at 0 (man-hours cannot be negative) plus 8% headroom above the
 * largest value, matching `cashFlowBarScale.ts#computeBarValueDomain`'s own headroom convention. */
export function computeManpowerBarDomain(values: readonly number[]): BarValueDomain {
  const maxValue = values.length === 0 ? 0 : Math.max(0, ...values)
  return { maxValue: maxValue > 0 ? maxValue * 1.08 : 1 }
}

/** Projects a value onto `[0, innerHeight]`, inverted (SVG y grows downward). */
export function scaleManpowerBarValue(value: number, domain: BarValueDomain, innerHeight: number): number {
  const span = Math.max(domain.maxValue, 1)
  return innerHeight - (Math.max(value, 0) / span) * innerHeight
}

export interface BarSlot {
  /** Left edge of this day's bar, in local (0..innerWidth) coordinates. */
  x: number
  width: number
}

/** Even categorical spacing for `count` daily bars across `innerWidth`. */
export function computeManpowerBarSlot(index: number, count: number, innerWidth: number, gapRatio = 0.28): BarSlot {
  const slot = innerWidth / Math.max(count, 1)
  const gap = slot * gapRatio
  return { x: index * slot + gap / 2, width: Math.max(slot - gap, 1) }
}
