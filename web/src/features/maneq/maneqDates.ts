/**
 * Pure calendar-day helpers for the Man/Equipment screen's PI queries. `ManpowerEquipmentLog.LogDate`
 * is a calendar-day identity normalised to project-timezone midnight (domain-rules.md §4.1), and PI
 * buckets are **half-open, lower-exclusive** — $(a, b]$ — over that same midnight grid (§4.5:
 * identical convention to `actual-cost.md` §7.5). All arithmetic here is done on UTC-midnight ISO
 * instants (mirrors `features/weather/weatherLabels.ts#toRequestDate`'s established convention), so
 * consecutive calendar days are always exactly 24h apart with no DST ambiguity — which is exactly
 * what `GetProductivityIndexQueryHandler.cs`'s manning-ratio gate requires
 * (`request.To - dayStart == TimeSpan.FromDays(1)`) to recognise a query as "exactly one day".
 */

/** `<input type="date">` value for "today", in the project's display timezone (matches
 * `features/weather/weatherForm.ts#todayDateInputValue`'s identical convention). */
export function todayDateInputValue(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok' }).format(new Date())
}

/** `"2026-07-08"` -> `"2026-07-08T00:00:00.000Z"` — UTC midnight of that calendar date. */
export function toRequestDate(dateInputValue: string): string {
  return new Date(`${dateInputValue}T00:00:00.000Z`).toISOString()
}

/** `"2026-07-08"` + `-1` -> `"2026-07-07"`; `+1` -> `"2026-07-09"`. */
export function addDaysToDateInputValue(dateInputValue: string, days: number): string {
  const date = new Date(`${dateInputValue}T00:00:00.000Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

export interface DateRequestBucket {
  /** Exclusive lower bound. */
  from: string
  /** Inclusive upper bound. */
  to: string
}

/** The exactly-one-calendar-day $(a, b]$ bucket for `dateInputValue` — `from` is the *previous* day's
 * midnight (so that day's own midnight-stamped rows are excluded) and `to` is this day's midnight
 * (so this day's own midnight-stamped rows are included, per the inclusive upper bound). This is the
 * one shape the backend also returns `manningRatio` for (`GetProductivityIndexQueryHandler.cs`). */
export function dayBucketRequest(dateInputValue: string): DateRequestBucket {
  return { from: toRequestDate(addDaysToDateInputValue(dateInputValue, -1)), to: toRequestDate(dateInputValue) }
}

/** `"2026-07-08"` -> `"2026-07-01"` — the first day of that month. */
export function startOfMonthDateInputValue(dateInputValue: string): string {
  return `${dateInputValue.slice(0, 7)}-01`
}

/** Month-to-date bucket ending at `dateInputValue`, inclusive of that day. */
export function monthToDateBucketRequest(dateInputValue: string): DateRequestBucket {
  const startOfMonth = startOfMonthDateInputValue(dateInputValue)
  return { from: toRequestDate(addDaysToDateInputValue(startOfMonth, -1)), to: toRequestDate(dateInputValue) }
}

/** Cumulative-from-project-start bucket ending at `dateInputValue` (§5.2's bottom formula, the KPI
 * tile) — `from` omitted entirely, matching `GetProductivityIndexQuery`'s own "null = project start"
 * convention (never a manufactured "far past" date on the wire). */
export function cumulativeBucketRequest(dateInputValue: string): { from: null; to: string } {
  return { from: null, to: toRequestDate(dateInputValue) }
}

/** The last `count` calendar days ending at (and including) `dateInputValue`, oldest first — the
 * histogram's x-axis. */
export function lastNDaysDateInputValues(dateInputValue: string, count: number): string[] {
  return Array.from({ length: count }, (_, index) => addDaysToDateInputValue(dateInputValue, -(count - 1 - index)))
}
