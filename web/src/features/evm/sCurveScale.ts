/**
 * Pure geometry/scale helpers for `components/SCurveChart.tsx` (S7-FE-04) — mirrors
 * `features/gantt/timeScale.ts`'s split between pure coordinate math (this file, plain numbers in
 * and out, no DOM/SVG) and the component that renders it, so the geometry is unit-testable without
 * mounting React/SVG at all.
 */

const ONE_DAY_MS = 24 * 60 * 60 * 1000

/**
 * How far past the last real data point the chart's time axis extends to make room for the dashed
 * EAC forecast tail. This is a **stylized** extension, not a real project finish date: no endpoint
 * this sprint's contract exposes a forecast *completion date* (`EvmResponseDto`/`EvmSnapshotDto`
 * carry a forecast *cost* — `variants[].eac` — but no date), so the forecast line's horizontal
 * endpoint is illustrative ("at completion, whenever that is") while its vertical position (the
 * EAC value itself) is the real, API-computed figure. See `SCurveChart.tsx`'s remarks.
 */
export const FORECAST_TIME_EXTENSION_RATIO = 0.25

export interface SCurveSeriesPoint {
  time: number
  pv: number
  ev: number
  ac: number
}

export interface SCurveTimeDomain {
  minTime: number
  /** The last *real* data point's time — the boundary between the solid historical/live lines and
   * the dashed forecast tail. */
  maxTime: number
  /** `maxTime` plus the stylized forecast extension — the dashed forecast line's x endpoint. */
  forecastTime: number
}

export interface SCurveValueDomain {
  minValue: number
  maxValue: number
}

/** Guards a degenerate (zero or one point) series so downstream division never hits a zero span —
 * a single-point project (no closed periods yet, only the live "today" point) is a real, expected
 * case, not an error. */
export function computeTimeDomain(times: readonly number[]): SCurveTimeDomain {
  if (times.length === 0) {
    const now = Date.now()
    return { minTime: now, maxTime: now, forecastTime: now + ONE_DAY_MS }
  }

  const minTime = Math.min(...times)
  const maxTime = Math.max(...times)
  const span = Math.max(maxTime - minTime, ONE_DAY_MS)
  const forecastTime = maxTime + span * FORECAST_TIME_EXTENSION_RATIO

  return { minTime, maxTime, forecastTime }
}

/**
 * Value (y) domain always starts at 0 (EVM cumulative curves never go negative) and stretches to
 * cover the largest of: any plotted PV/EV/AC point, `bac` (the reference gridline), and the
 * forecast EAC value (when computable) — plus 8% headroom so the topmost line/label is never
 * clipped against the card edge.
 */
export function computeValueDomain(
  points: readonly Pick<SCurveSeriesPoint, 'pv' | 'ev' | 'ac'>[],
  bac: number,
  forecastEac: number | null,
): SCurveValueDomain {
  const values = points.flatMap((p) => [p.pv, p.ev, p.ac])
  values.push(0, bac)
  if (forecastEac !== null && Number.isFinite(forecastEac)) values.push(forecastEac)

  const maxValue = Math.max(...values)
  return { minValue: 0, maxValue: maxValue > 0 ? maxValue * 1.08 : 1 }
}

/** Projects a time value onto `[0, innerWidth]` given the time domain — the domain spans
 * `[minTime, forecastTime]` (not `maxTime`) so the forecast tail always fits on-chart. */
export function scaleTime(time: number, domain: SCurveTimeDomain, innerWidth: number): number {
  const span = Math.max(domain.forecastTime - domain.minTime, 1)
  return ((time - domain.minTime) / span) * innerWidth
}

/** Projects a money value onto `[0, innerHeight]`, inverted (SVG y grows downward, so a larger
 * value must plot *higher*, i.e. a smaller y). */
export function scaleValue(value: number, domain: SCurveValueDomain, innerHeight: number): number {
  const span = Math.max(domain.maxValue - domain.minValue, 1)
  return innerHeight - ((value - domain.minValue) / span) * innerHeight
}

/** Builds an SVG `<path>` `d` attribute connecting real data points with straight segments
 * (`M`/`L`) — deliberately never a curve-fit/Bezier smoothing: a smoothed curve would visually imply
 * a value existed *between* two actually-sampled period dates that was never measured, which is
 * exactly the kind of fabricated-precision this whole feature must avoid (see this sprint's report
 * on the prototype's decorative Bezier mock vs. this real implementation). Returns `''` for an empty
 * input (nothing to draw). */
export function buildPolylinePath(points: readonly { x: number; y: number }[]): string {
  return points.map((p, index) => `${index === 0 ? 'M' : 'L'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ')
}
