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
