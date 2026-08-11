/**
 * Pure display-banding rules for the Man/Equipment screen (domain-rules.md §5.3/§9.2). Kept separate
 * from any component so both bandings — which this task's brief explicitly calls out as easy to get
 * wrong — are directly unit-testable against the domain document's own worked fixtures.
 */

/** Parses a wire decimal-as-string field; every money-free decimal in this feature (`manHours`,
 * `productivityIndex`, `coveragePercentage`, ...) arrives this way (project-wide
 * `DecimalAsStringJsonConverter` — see `types.ts`'s own remarks). Returns `null` for a JSON `null`. */
export function parseManpowerDecimal(value: string | null): number | null {
  if (value === null) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

export type PiBand = 'success' | 'gold' | 'danger' | 'null'

/**
 * domain-rules.md §5.3's colour table — **higher PI is better**, `1.00` is exactly on budget:
 * | PI ≥ 0.95 -> success (ตามแผน/ดีกว่าแผน) | 0.85 ≤ PI < 0.95 -> gold (ต่ำกว่าแผนเล็กน้อย) |
 * | PI < 0.85 -> danger (ต่ำกว่าแผนมาก) | `null` -> its own band, rendered "—" with the reason. |
 *
 * This is the *opposite* shape from `manningVarianceBand` below on purpose — conflating the two
 * bandings (or worse, conflating the two *values*) is exactly the defect this whole feature exists
 * to avoid (§5.1, fixture M-02: manningRatio 1.25 "green" on a day PI is really 0.60 "red").
 */
export function piBand(value: number | null): PiBand {
  if (value === null) return 'null'
  if (value >= 0.95) return 'success'
  if (value >= 0.85) return 'gold'
  return 'danger'
}

export type ManningVarianceBand = 'onplan' | 'below' | 'above' | 'noplan'

/**
 * domain-rules.md §9.2's histogram bar-colour rule, applied to the manning (headcount) delta — a
 * **neutral** palette, never good/bad, because over-manning is not good news (§5.3's flagged
 * prototype defect at `ECM Planning Prototype.dc.html:872`: `dColor: d >= -10 ? green : red` paints
 * *any* shortfall under 10 heads green and, worse, paints unlimited over-manning green too — this
 * function is this feature's fix for that specific defect, reused for both the KPI tile's "กำลังคน
 * วันนี้" delta and the histogram's bar colour):
 * | within ±5% of plan -> onplan (secondary slate-blue) | >5% below plan -> below (danger red) |
 * | >5% above plan -> above (gold — **never green**) | no plan configured -> noplan (neutral, "—") |
 */
export function manningVarianceBand(actualWorkerCount: number, plannedWorkerCount: number | null): ManningVarianceBand {
  if (plannedWorkerCount === null || plannedWorkerCount === 0) return 'noplan'
  const ratio = actualWorkerCount / plannedWorkerCount
  if (ratio >= 0.95 && ratio <= 1.05) return 'onplan'
  return ratio < 0.95 ? 'below' : 'above'
}

/** `advisoryDataQualityWarnings` outside [0.20, 3.00] per §5.3 — informational only, mirrored here so
 * a caller does not need to re-derive the threshold; never gates or recolours anything by itself
 * (the `ImplausiblePi` warning the backend already attaches is the authoritative signal — this is a
 * pure convenience for a client-only presentation decision, e.g. an extra icon). */
export function isImplausiblePi(value: number | null): boolean {
  if (value === null) return false
  return value < 0.2 || value > 3.0
}
