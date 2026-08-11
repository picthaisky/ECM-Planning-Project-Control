import { describe, expect, it } from 'vitest'
import {
  addDaysToDateInputValue,
  cumulativeBucketRequest,
  dayBucketRequest,
  lastNDaysDateInputValues,
  monthToDateBucketRequest,
  startOfMonthDateInputValue,
  toRequestDate,
} from './maneqDates'

describe('toRequestDate', () => {
  it('produces UTC midnight for the given calendar date', () => {
    expect(toRequestDate('2026-07-08')).toBe('2026-07-08T00:00:00.000Z')
  })
})

describe('addDaysToDateInputValue', () => {
  it('adds/subtracts days without DST/timezone drift (UTC-anchored)', () => {
    expect(addDaysToDateInputValue('2026-07-08', 1)).toBe('2026-07-09')
    expect(addDaysToDateInputValue('2026-07-08', -1)).toBe('2026-07-07')
    expect(addDaysToDateInputValue('2026-07-08', 0)).toBe('2026-07-08')
  })

  it('crosses a month/year boundary correctly', () => {
    expect(addDaysToDateInputValue('2026-07-31', 1)).toBe('2026-08-01')
    expect(addDaysToDateInputValue('2026-01-01', -1)).toBe('2025-12-31')
  })
})

describe('dayBucketRequest', () => {
  it('produces an exactly-24h (a,b] bucket: from = previous midnight, to = this midnight', () => {
    const bucket = dayBucketRequest('2026-07-08')
    expect(bucket.from).toBe('2026-07-07T00:00:00.000Z')
    expect(bucket.to).toBe('2026-07-08T00:00:00.000Z')

    const fromMs = new Date(bucket.from).getTime()
    const toMs = new Date(bucket.to).getTime()
    // This exact width is what `GetProductivityIndexQueryHandler.cs` checks
    // (`request.To - dayStart == TimeSpan.FromDays(1)`) to decide whether to also return manningRatio.
    expect(toMs - fromMs).toBe(24 * 60 * 60 * 1000)
  })

  it("includes the day's own midnight-stamped LogDate under the (a,b] convention, excludes the previous day's", () => {
    // A row with LogDate exactly at "to" satisfies `a < LogDate <= b` (included);
    // a row with LogDate exactly at "from" (the previous day) does not (`a < LogDate` is false).
    const bucket = dayBucketRequest('2026-07-08')
    const from = new Date(bucket.from).getTime()
    const to = new Date(bucket.to).getTime()

    expect(from < to && to <= to).toBe(true)
    expect(from < from).toBe(false)
  })
})

describe('startOfMonthDateInputValue', () => {
  it('returns the first day of the month', () => {
    expect(startOfMonthDateInputValue('2026-07-18')).toBe('2026-07-01')
  })
})

describe('monthToDateBucketRequest', () => {
  it('spans from the day before month-start through the given day, inclusive', () => {
    const bucket = monthToDateBucketRequest('2026-07-18')
    expect(bucket.from).toBe('2026-06-30T00:00:00.000Z')
    expect(bucket.to).toBe('2026-07-18T00:00:00.000Z')
  })
})

describe('cumulativeBucketRequest', () => {
  it('omits "from" entirely (never a manufactured far-past date) and sets "to" to the given day', () => {
    expect(cumulativeBucketRequest('2026-07-18')).toEqual({ from: null, to: '2026-07-18T00:00:00.000Z' })
  })
})

describe('lastNDaysDateInputValues', () => {
  it('returns the last N calendar days, oldest first, ending with the given day', () => {
    expect(lastNDaysDateInputValues('2026-07-08', 7)).toEqual([
      '2026-07-02',
      '2026-07-03',
      '2026-07-04',
      '2026-07-05',
      '2026-07-06',
      '2026-07-07',
      '2026-07-08',
    ])
  })

  it('returns exactly one day when count is 1', () => {
    expect(lastNDaysDateInputValues('2026-07-08', 1)).toEqual(['2026-07-08'])
  })
})
