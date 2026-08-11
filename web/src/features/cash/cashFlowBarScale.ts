/**
 * Pure geometry/scale helpers for `components/CashFlowBarChart.tsx` — mirrors
 * `features/evm/sCurveScale.ts`'s split between pure coordinate math (this file, plain numbers in
 * and out, no DOM/SVG) and the component that renders it, so the geometry is unit-testable without
 * mounting React/SVG at all.
 */

export interface CashFlowBarValueDomain {
  minValue: number
  maxValue: number
}

/**
 * Value (y) domain always includes 0 (the baseline every bar grows from) plus 8% headroom above the
 * largest value, same convention as `sCurveScale.ts#computeValueDomain`. Extends *below* zero too
 * when a period figure is genuinely negative (actual-cost.md §7.6: a period AC over-reversal is a
 * real, if rare, case — this chart renders it honestly rather than clamping it to 0).
 */
export function computeBarValueDomain(values: readonly number[]): CashFlowBarValueDomain {
  if (values.length === 0) {
    return { minValue: 0, maxValue: 1 }
  }

  const minValue = Math.min(0, ...values)
  const maxValue = Math.max(0, ...values)
  const span = maxValue - minValue

  if (span > 0) {
    return { minValue, maxValue: maxValue + span * 0.08 }
  }
  // Degenerate: every value is exactly 0 (a genuinely empty/sparse period, task note #3) — still a
  // valid domain, never a divide-by-zero downstream.
  return { minValue, maxValue: 1 }
}

/** Projects a value onto `[0, innerHeight]`, inverted (SVG y grows downward, so a larger value must
 * plot *higher*, i.e. a smaller y). Mirrors `sCurveScale.ts#scaleValue`. */
export function scaleBarValue(value: number, domain: CashFlowBarValueDomain, innerHeight: number): number {
  const span = Math.max(domain.maxValue - domain.minValue, 1)
  return innerHeight - ((value - domain.minValue) / span) * innerHeight
}

/** The y position of the zero baseline — every bar grows up from here when positive, down from here
 * when negative (only visually relevant when `domain.minValue < 0`). */
export function zeroBaselineY(domain: CashFlowBarValueDomain, innerHeight: number): number {
  return scaleBarValue(0, domain, innerHeight)
}

export interface BarGroupLayout {
  /** Left edge of this period's 3-bar cluster, in local (0..innerWidth) coordinates. */
  groupX: number
  /** Width of the whole 3-bar cluster (after the inter-group gap is subtracted). */
  groupWidth: number
  /** Width of one single bar (PV, EV or AC) inside the cluster. */
  barWidth: number
}

/**
 * Even categorical spacing for `groupCount` period clusters across `innerWidth`, each cluster
 * holding exactly 3 bars (PV/EV/AC) — a *sparse* chart (task note #3: "until someone closes a
 * period the chart shows essentially one bar") is not a special case here, it is simply
 * `groupCount === 1`, which still produces a well-formed, centered, reasonably-sized single cluster
 * rather than a degenerate empty/giant bar.
 */
export function computeGroupLayout(
  index: number,
  groupCount: number,
  innerWidth: number,
  groupGapRatio = 0.32,
): BarGroupLayout {
  const groupSlot = innerWidth / Math.max(groupCount, 1)
  const gap = groupSlot * groupGapRatio
  const groupWidth = Math.max(groupSlot - gap, 1)
  const barWidth = groupWidth / 3

  return { groupX: index * groupSlot + gap / 2, groupWidth, barWidth }
}
