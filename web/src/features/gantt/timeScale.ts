/**
 * Pure date/pixel geometry for the Gantt chart (S6-FE-01/02, US-6.1). Nothing here touches the
 * DOM/canvas or React — every function is a deterministic mapping so it can be unit-tested without
 * a browser and reused identically by `canvasDraw.ts` (bars, data-date line) and the header ticks.
 *
 * Deliberately a *linear days-since-timeline-start* scale rather than a calendar-aware one (e.g.
 * variable month widths) — this keeps `dateToX`/`xToDate` trivially invertible (needed for the
 * zoom reference-point preservation, S6-FE-02 DoD) and geometrically exact for the data-date line,
 * at the cost of month columns not being perfectly proportional to each month's real day count at
 * the "month" zoom level (a cosmetic simplification, not a correctness issue for this sprint's DoD).
 */

export type ZoomLevel = 'day' | 'week' | 'month'

export const ZOOM_LEVELS: readonly ZoomLevel[] = ['day', 'week', 'month']

export const ZOOM_LABELS: Record<ZoomLevel, string> = {
  day: 'วัน',
  week: 'สัปดาห์',
  month: 'เดือน',
}

/** Pixels-per-day at each zoom level. Chosen so a "day" view comfortably shows a day-number tick
 * per column, "week" shows ~7 columns per week label, and "month" fits a multi-year schedule in a
 * reasonably scrollable width — no DoD-mandated exact value, just three visibly distinct scales. */
export const PX_PER_DAY: Record<ZoomLevel, number> = {
  day: 32,
  week: 10,
  month: 3,
}

export const ROW_HEIGHT = 44
export const HEADER_HEIGHT = 28
export const LABEL_PANE_WIDTH = 250
export const BAR_TOP = 8
export const BAR_HEIGHT = 13

const MS_PER_DAY = 24 * 60 * 60 * 1000

/** Days of padding rendered before the earliest and after the latest activity date, so the first
 * and last bars are never flush against the scrollable edge. */
export const TIMELINE_PADDING_DAYS = 14

export function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

export function addDays(d: Date, days: number): Date {
  const next = new Date(d)
  next.setDate(next.getDate() + days)
  return next
}

/** Whole-day difference `b - a`, ignoring time-of-day (both dates truncated to local midnight
 * first) so DST transitions never introduce a fractional-day rounding error into bar geometry. */
export function daysBetween(a: Date, b: Date): number {
  return Math.round((startOfDay(b).getTime() - startOfDay(a).getTime()) / MS_PER_DAY)
}

export interface TimelineBounds {
  start: Date
  end: Date
}

export interface ActivitySpan {
  plannedStart: string
  plannedFinish: string
  actualStart: string | null
  actualFinish: string | null
}

/** The overall scrollable date range for a Gantt: the earliest of every activity's
 * planned/actual start and the latest of every planned/actual finish, padded by
 * `TIMELINE_PADDING_DAYS` on each side. An empty activity list falls back to "today +/- padding"
 * so the chart still renders a sane, non-degenerate scale rather than a zero-width timeline. */
export function computeTimelineBounds(activities: readonly ActivitySpan[]): TimelineBounds {
  if (activities.length === 0) {
    const today = startOfDay(new Date())
    return { start: addDays(today, -TIMELINE_PADDING_DAYS), end: addDays(today, TIMELINE_PADDING_DAYS) }
  }

  let minTime = Number.POSITIVE_INFINITY
  let maxTime = Number.NEGATIVE_INFINITY

  for (const activity of activities) {
    for (const iso of [activity.plannedStart, activity.plannedFinish, activity.actualStart, activity.actualFinish]) {
      if (!iso) continue
      const t = new Date(iso).getTime()
      if (Number.isNaN(t)) continue
      if (t < minTime) minTime = t
      if (t > maxTime) maxTime = t
    }
  }

  if (!Number.isFinite(minTime) || !Number.isFinite(maxTime)) {
    const today = startOfDay(new Date())
    return { start: addDays(today, -TIMELINE_PADDING_DAYS), end: addDays(today, TIMELINE_PADDING_DAYS) }
  }

  return {
    start: addDays(startOfDay(new Date(minTime)), -TIMELINE_PADDING_DAYS),
    end: addDays(startOfDay(new Date(maxTime)), TIMELINE_PADDING_DAYS),
  }
}

/** Maps a date (ISO string or `Date`) to an x-pixel offset from the timeline's own start — the
 * single function both bar geometry and the data-date line rely on for "geometrically correct
 * x-position" (S6-FE-01 DoD). */
export function dateToX(iso: string | Date, timelineStart: Date, pxPerDay: number): number {
  const d = typeof iso === 'string' ? new Date(iso) : iso
  return daysBetween(timelineStart, d) * pxPerDay
}

/** Inverse of `dateToX` — an x-pixel offset back to a whole-day `Date`. Used to compute the "focal
 * date" under the viewport center when preserving scroll reference across a zoom change. */
export function xToDate(x: number, timelineStart: Date, pxPerDay: number): Date {
  return addDays(timelineStart, x / pxPerDay)
}

export function totalContentWidth(bounds: TimelineBounds, pxPerDay: number): number {
  return Math.max(daysBetween(bounds.start, bounds.end) * pxPerDay, 1)
}

export interface HeaderTick {
  x: number
  label: string
  /** Month-boundary (month/day zoom) or first-week-of-month (week zoom) ticks render slightly
   * bolder/darker, matching the prototype's month-header emphasis. */
  isMajor: boolean
}

const THAI_MONTHS_SHORT = [
  'ม.ค.',
  'ก.พ.',
  'มี.ค.',
  'เม.ย.',
  'พ.ค.',
  'มิ.ย.',
  'ก.ค.',
  'ส.ค.',
  'ก.ย.',
  'ต.ค.',
  'พ.ย.',
  'ธ.ค.',
] as const

/** Buddhist-era 2-digit year suffix (matches the prototype's "11 ก.ค. 2569" Thai-calendar
 * convention, abbreviated). */
function thaiYearSuffix(gregorianYear: number): string {
  return String(gregorianYear + 543).slice(-2)
}

/** Header tick marks for the time-axis, one set per zoom level. Pure and bounded by the timeline's
 * own day span (never by activity count), so a multi-year "day" zoom produces at most a few
 * thousand ticks — still nowhere near the 10,000+ *activity* scale ADR-0004 targets, and (per
 * `canvasDraw.ts`) drawn on the same canvas as the bars, never one DOM node per tick either. */
export function getHeaderTicks(zoom: ZoomLevel, bounds: TimelineBounds, pxPerDay: number): HeaderTick[] {
  const ticks: HeaderTick[] = []

  if (zoom === 'month') {
    const cursor = new Date(bounds.start.getFullYear(), bounds.start.getMonth(), 1)
    while (cursor < bounds.end) {
      const isJanuary = cursor.getMonth() === 0
      const label = isJanuary
        ? `${THAI_MONTHS_SHORT[cursor.getMonth()]} ${thaiYearSuffix(cursor.getFullYear())}`
        : THAI_MONTHS_SHORT[cursor.getMonth()]
      ticks.push({ x: dateToX(cursor, bounds.start, pxPerDay), label, isMajor: isJanuary })
      cursor.setMonth(cursor.getMonth() + 1)
    }
    return ticks
  }

  if (zoom === 'week') {
    const cursor = startOfDay(bounds.start)
    const isoDow = (cursor.getDay() + 6) % 7 // Monday = 0 .. Sunday = 6
    cursor.setDate(cursor.getDate() - isoDow)
    while (cursor < bounds.end) {
      const label = `${cursor.getDate()} ${THAI_MONTHS_SHORT[cursor.getMonth()]}`
      ticks.push({ x: dateToX(cursor, bounds.start, pxPerDay), label, isMajor: cursor.getDate() <= 7 })
      cursor.setDate(cursor.getDate() + 7)
    }
    return ticks
  }

  // day
  const cursor = startOfDay(bounds.start)
  while (cursor < bounds.end) {
    const isMonthStart = cursor.getDate() === 1
    const label = isMonthStart ? `${cursor.getDate()} ${THAI_MONTHS_SHORT[cursor.getMonth()]}` : String(cursor.getDate())
    ticks.push({ x: dateToX(cursor, bounds.start, pxPerDay), label, isMajor: isMonthStart })
    cursor.setDate(cursor.getDate() + 1)
  }
  return ticks
}

export interface ComputeScrollLeftForZoomChangeParams {
  oldScrollLeft: number
  oldPxPerDay: number
  newPxPerDay: number
  viewportWidth: number
}

/**
 * S6-FE-02 DoD ("เปลี่ยน zoom ไม่ทำให้ตำแหน่งอ้างอิงหาย" — changing zoom must not lose the user's
 * current reference point): finds the date currently at the horizontal center of the viewport
 * under the *old* scale, then returns the `scrollLeft` that puts that same date back at the
 * viewport center under the *new* scale. Pure pixel arithmetic — deliberately independent of
 * `timelineStart` (it cancels out algebraically), so it never needs a `Date` at all and is trivial
 * to unit test.
 */
export function computeScrollLeftForZoomChange({
  oldScrollLeft,
  oldPxPerDay,
  newPxPerDay,
  viewportWidth,
}: ComputeScrollLeftForZoomChangeParams): number {
  const focalDayOffset = (oldScrollLeft + viewportWidth / 2) / oldPxPerDay
  return focalDayOffset * newPxPerDay - viewportWidth / 2
}
