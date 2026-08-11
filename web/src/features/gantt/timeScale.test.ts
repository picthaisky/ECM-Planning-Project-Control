import { describe, expect, it } from 'vitest'
import {
  computeScrollLeftForZoomChange,
  computeTimelineBounds,
  daysBetween,
  dateToX,
  getHeaderTicks,
  totalContentWidth,
  xToDate,
  PX_PER_DAY,
  TIMELINE_PADDING_DAYS,
} from './timeScale'
import type { ActivitySpan } from './timeScale'

function activity(overrides: Partial<ActivitySpan>): ActivitySpan {
  return {
    plannedStart: '2026-01-01T00:00:00+07:00',
    plannedFinish: '2026-01-10T00:00:00+07:00',
    actualStart: null,
    actualFinish: null,
    ...overrides,
  }
}

describe('computeTimelineBounds', () => {
  it('spans the earliest planned/actual start to the latest planned/actual finish, padded by TIMELINE_PADDING_DAYS', () => {
    const activities: ActivitySpan[] = [
      activity({ plannedStart: '2026-03-01T00:00:00+07:00', plannedFinish: '2026-03-15T00:00:00+07:00' }),
      activity({
        plannedStart: '2026-02-01T00:00:00+07:00',
        plannedFinish: '2026-02-20T00:00:00+07:00',
        actualStart: '2026-01-28T00:00:00+07:00',
        actualFinish: '2026-04-01T00:00:00+07:00', // actual finish is the true latest date here
      }),
    ]

    const bounds = computeTimelineBounds(activities)

    expect(daysBetween(bounds.start, new Date('2026-01-28T00:00:00+07:00'))).toBe(TIMELINE_PADDING_DAYS)
    expect(daysBetween(new Date('2026-04-01T00:00:00+07:00'), bounds.end)).toBe(TIMELINE_PADDING_DAYS)
  })

  it('falls back to a sane, non-degenerate range around today when there are no activities', () => {
    const bounds = computeTimelineBounds([])
    expect(daysBetween(bounds.start, bounds.end)).toBe(TIMELINE_PADDING_DAYS * 2)
  })
})

describe('dateToX / xToDate (geometry the data-date line and bars both rely on)', () => {
  it('is exactly invertible and linear in pxPerDay', () => {
    const timelineStart = new Date(2026, 0, 1)
    const pxPerDay = PX_PER_DAY.week

    const x = dateToX('2026-02-15T00:00:00+07:00', timelineStart, pxPerDay)
    expect(x).toBe(45 * pxPerDay) // 2026-01-01 -> 2026-02-15 is 45 whole days

    const roundTripped = xToDate(x, timelineStart, pxPerDay)
    expect(daysBetween(timelineStart, roundTripped)).toBe(45)
  })

  it('places the data-date line at the geometrically correct x position for a known date/scale (S6-FE-01 DoD)', () => {
    const timelineStart = new Date(2026, 0, 1)
    const pxPerDay = PX_PER_DAY.day // 32 px/day

    // 10 days after timelineStart, at 32px/day, must be exactly 320px from the origin.
    const x = dateToX('2026-01-11T00:00:00+07:00', timelineStart, pxPerDay)
    expect(x).toBe(320)
  })
})

describe('totalContentWidth', () => {
  it('is the whole-day span times pxPerDay, never zero even for a same-day bound', () => {
    const bounds = { start: new Date(2026, 0, 1), end: new Date(2026, 0, 1) }
    expect(totalContentWidth(bounds, 10)).toBe(1) // clamped to a minimum of 1px, never 0/negative
  })
})

describe('getHeaderTicks', () => {
  const bounds = { start: new Date(2026, 0, 1), end: new Date(2026, 2, 1) } // Jan 1 - Mar 1 2026

  it('month zoom: one tick per calendar month, January ticks carry a 2-digit Buddhist-era year', () => {
    const ticks = getHeaderTicks('month', bounds, PX_PER_DAY.month)
    expect(ticks.map((t) => t.label)).toEqual(['ม.ค. 69', 'ก.พ.'])
    expect(ticks[0].isMajor).toBe(true)
    expect(ticks[1].isMajor).toBe(false)
  })

  it('day zoom: one tick per calendar day, spanning the whole bounds', () => {
    const ticks = getHeaderTicks('day', bounds, PX_PER_DAY.day)
    // Jan (31) + Feb 2026 (28, not a leap year) = 59 days from Jan 1 up to (excluding) Mar 1.
    expect(ticks).toHaveLength(59)
    expect(ticks[0].label).toBe('1 ม.ค.')
    expect(ticks[0].isMajor).toBe(true)
  })

  it('week zoom: ticks are 7 days apart', () => {
    const ticks = getHeaderTicks('week', bounds, PX_PER_DAY.week)
    expect(ticks.length).toBeGreaterThan(1)
    const spacingDays = (ticks[1].x - ticks[0].x) / PX_PER_DAY.week
    expect(spacingDays).toBe(7)
  })
})

describe('computeScrollLeftForZoomChange (S6-FE-02 DoD: zoom must preserve the reference point)', () => {
  it('keeps the same focal date at the viewport center when pxPerDay changes', () => {
    const oldPxPerDay = PX_PER_DAY.week
    const newPxPerDay = PX_PER_DAY.month
    const viewportWidth = 800
    const oldScrollLeft = 500

    const focalDayOffset = (oldScrollLeft + viewportWidth / 2) / oldPxPerDay

    const newScrollLeft = computeScrollLeftForZoomChange({
      oldScrollLeft,
      oldPxPerDay,
      newPxPerDay,
      viewportWidth,
    })

    // Re-deriving the focal day from the *new* scale must reproduce the same day offset.
    const newFocalDayOffset = (newScrollLeft + viewportWidth / 2) / newPxPerDay
    expect(newFocalDayOffset).toBeCloseTo(focalDayOffset, 10)
  })

  it('is a no-op when zoom does not actually change', () => {
    const result = computeScrollLeftForZoomChange({
      oldScrollLeft: 240,
      oldPxPerDay: PX_PER_DAY.week,
      newPxPerDay: PX_PER_DAY.week,
      viewportWidth: 800,
    })
    expect(result).toBe(240)
  })
})
