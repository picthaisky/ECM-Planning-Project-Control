import { roundHalfAwayFromZero } from '../../utils/format'

/**
 * Retention-cap math for the Project Info screen (S4-FE-02 DoD, risk R-02).
 *
 * `.claude/knowledge/domain/payment-retention.md` §2 flags a real bug in the prototype: it shows
 * "Retention 5% (เพดาน 10% ของสัญญา)" — a flat 5%-per-payment rate can never accumulate past 5% of
 * contract value, so pairing it with a 10% cap is internally inconsistent (the 10% figure is
 * unreachable and therefore meaningless as a "ceiling"). The DoD requires the displayed ceiling to
 * be **actually calculated** from `Project.ContractValue` × `Project.RetentionCapPercentage`
 * (`R^max` in the payment-math fixtures) instead of echoing that static, occasionally-impossible
 * prototype example.
 *
 * `NORMAL_RETENTION_CAP_WARNING_PCT` is a *different* number serving a *different* purpose: the
 * DoD's "exceeding the normal ceiling (prototype's 10% example) shows a non-blocking warning" is
 * read here as "10% is unusually high for a `RetentionCapPercentage` input, so warn" — a soft
 * sanity check on the configured rate, never a hard limit and never conflated with the computed
 * money amount above (that would silently reproduce the same rate-vs-cap coupling bug this module
 * exists to fix).
 */
export const NORMAL_RETENTION_CAP_WARNING_PCT = 10

/**
 * `R^max = (c / 100) * C`, `null` when `retentionCapPercentage` is `null` (uncapped — the Thai-
 * standard contract shape, payment-retention.md §2). Rounded half-away-from-zero to 2dp, matching
 * every other money figure in this codebase (never a difference computation at full precision).
 */
export function calculateRetentionCapAmount(
  contractValue: number,
  retentionCapPercentage: number | null,
): number | null {
  if (retentionCapPercentage === null) return null
  return roundHalfAwayFromZero((retentionCapPercentage / 100) * contractValue, 2)
}

/** Non-blocking warning trigger only — see this module's doc comment. */
export function isAboveNormalRetentionCapThreshold(retentionCapPercentage: number | null): boolean {
  return retentionCapPercentage !== null && retentionCapPercentage > NORMAL_RETENTION_CAP_WARNING_PCT
}
