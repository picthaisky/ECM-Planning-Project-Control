/**
 * Shared money/percent formatting + rounding helpers (CLAUDE.md "Numbers" non-negotiable: money
 * with 2 decimals and thousand separators; percentages 2 decimals; every calculation mirrors the
 * backend's rounding rule — never compute a domain formula differently from `domain-rules.md`).
 *
 * Backend rounding trap (`.claude/knowledge/domain/payment-retention.md` fixture P7): .NET's
 * `Math.Round(x, 2)` defaults to banker's rounding, so every money-adjacent frontend calculation
 * (e.g. the S4-FE-02 retention-cap preview) must round **half-away-from-zero** the same way the
 * backend is required to (`MidpointRounding.AwayFromZero`), or the two sides diverge on the exact
 * midpoint values that fixture exists to catch.
 */

/** Rounds `value` to `decimals` places using half-away-from-zero (never JS's native banker's-ish
 * floating point rounding, and never `Math.round`'s "round half up" which is wrong for negatives). */
export function roundHalfAwayFromZero(value: number, decimals: number): number {
  const factor = 10 ** decimals
  const sign = value < 0 ? -1 : 1
  return (sign * Math.round(Math.abs(value) * factor)) / factor
}

const THB_MONEY_FORMATTER = new Intl.NumberFormat('th-TH', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/** `1234567.5` -> `"1,234,567.50"` (thousand separators, always exactly 2 decimals). Accepts a
 * `string` too since API money fields are transported as decimal-safe strings (conventions.md). */
export function formatMoney(value: number | string): string {
  const numeric = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numeric)) return '—'
  return THB_MONEY_FORMATTER.format(roundHalfAwayFromZero(numeric, 2))
}

/** `5` -> `"5.00%"`, `12.345` -> `"12.35%"` — always exactly `decimals` places (default 2, per the
 * `decimal(5,2)` percent fields this sprint edits: Retention/Advance/Cap rate). */
export function formatPercent(value: number | string, decimals = 2): string {
  const numeric = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numeric)) return '—'
  return `${roundHalfAwayFromZero(numeric, decimals).toFixed(decimals)}%`
}

/**
 * Plain-number formatter for ratio-shaped EVM metrics (SPI/CPI/TCPI/Performance Factor) — no
 * thousand separator (ratios are never in the thousands), no unit suffix, always exactly
 * `decimals` places (default 2, matching the prototype's SPI/CPI cadence — `0.92`, `1.04`).
 *
 * The API wire value carries 6 decimal digits (`RoundingRules.RatioDecimals`,
 * `backend/src/CMPlus.Domain/Common/RoundingRules.cs`) so the audit trail stays exact — this
 * formatter only controls *display* rounding, the same "round once at the display boundary,
 * never re-derive the number" rule `formatMoney`/`formatPercent` already follow.
 */
export function formatRatio(value: number | string, decimals = 2): string {
  const numeric = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numeric)) return '—'
  return roundHalfAwayFromZero(numeric, decimals).toFixed(decimals)
}

const MILLIONS_FORMATTER = new Intl.NumberFormat('th-TH', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/**
 * Compact "million baht" formatter for executive-glance screens (S8-FE-01 Dashboard, S8-FE-02 Cash
 * Flow) — `485000000` -> `"485.00 MB"`. This is the prototype's own display convention for these two
 * screens specifically (`docs/ECM Planning Prototype.dc.html`'s Dashboard/Cash Flow tiles: "466 MB",
 * "238.4 MB"; `.claude/knowledge/domain/actual-cost.md` §5 cites the same "MB" unit for the Cash Flow
 * tiles verbatim) — kept as `MB` (not translated to "ล้านบาท") to match that source-of-truth text
 * exactly, same discipline as `navConfig.ts`'s "exact label text from the prototype — do not
 * translate/shorten" rule.
 *
 * Deliberately a *different* precision convention from `formatMoney` (full baht, 2dp, thousand
 * separators) — `features/evm/`'s detail screen shows exact baht (audit-grade precision matters
 * there); an executive dashboard/cash-flow summary shows a glanceable, rounded-to-the-nearest-
 * ten-thousand-baht figure instead. Both still obey the CLAUDE.md "money: 2 decimals" rule — this
 * only rescales the *unit* (baht -> million baht), it never changes how many significant decimal
 * places are shown, and it is a pure display transform (no domain formula, no different rounding
 * *rule* than `formatMoney`'s own half-away-from-zero — see that function's own remarks).
 */
export function formatMoneyMillions(value: number | string): string {
  const numeric = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numeric)) return '—'
  return `${MILLIONS_FORMATTER.format(roundHalfAwayFromZero(numeric / 1_000_000, 2))} MB`
}
