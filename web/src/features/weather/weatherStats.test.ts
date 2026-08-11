import { describe, expect, it } from 'vitest'
import { computeWeatherSummaryStats } from './weatherStats'
import type { WeatherLogDto } from './types'

function makeLog(overrides: Partial<WeatherLogDto> & { id: string }): WeatherLogDto {
  return {
    projectId: 'project-1',
    logDate: '2026-07-08T00:00:00Z',
    condition: 'HeavyRain',
    conditionNote: null,
    rainfallMm: '61.00',
    impact: 'FullStoppage',
    impactNote: null,
    hoursLost: '8.00',
    workStoppage: true,
    entryKind: 'Original',
    correctsWeatherLogId: null,
    correctionReason: null,
    affectedActivityIds: ['activity-1'],
    recordedByUserId: 'user-1',
    recordedAt: '2026-07-08T09:00:00Z',
    ...overrides,
  }
}

describe('features/weather/weatherStats', () => {
  it('counts distinct rain days and stoppage days among in-force entries', () => {
    const logs = [
      makeLog({ id: 'e1', logDate: '2026-07-08T00:00:00Z', condition: 'HeavyRain', workStoppage: true }),
      makeLog({ id: 'e2', logDate: '2026-07-09T00:00:00Z', condition: 'Clear', rainfallMm: null, impact: 'NoImpact', workStoppage: false }),
    ]

    const stats = computeWeatherSummaryStats(logs)
    expect(stats.rainDayCount).toBe(1)
    expect(stats.stoppageDayCount).toBe(1)
    expect(stats.unattributedCount).toBe(0)
  })

  it('counts a measured rainfall depth as a rain day even under Clear/Cloudy/Other', () => {
    const logs = [makeLog({ id: 'e1', condition: 'Other', rainfallMm: '5.50' })]
    expect(computeWeatherSummaryStats(logs).rainDayCount).toBe(1)
  })

  it('does not count a zero/absent rainfall Clear day as a rain day', () => {
    const logs = [makeLog({ id: 'e1', condition: 'Clear', rainfallMm: null, impact: 'NoImpact', workStoppage: false })]
    expect(computeWeatherSummaryStats(logs).rainDayCount).toBe(0)
  })

  it('does not double-count the same calendar day across two entries', () => {
    const logs = [
      makeLog({ id: 'e1', logDate: '2026-07-08T00:00:00Z' }),
      makeLog({ id: 'e2', logDate: '2026-07-08T12:00:00Z', affectedActivityIds: ['activity-2'] }),
    ]
    const stats = computeWeatherSummaryStats(logs)
    expect(stats.rainDayCount).toBe(1)
    expect(stats.stoppageDayCount).toBe(1)
  })

  // fixture W-09: an impacted entry naming no activity is legitimate evidence but unattributed.
  it('counts an impacted entry with no affected activities as unattributed', () => {
    const logs = [makeLog({ id: 'e1', affectedActivityIds: [] })]
    expect(computeWeatherSummaryStats(logs).unattributedCount).toBe(1)
  })

  it('does not count a NoImpact entry with no activities as unattributed (nothing to attribute)', () => {
    const logs = [makeLog({ id: 'e1', impact: 'NoImpact', workStoppage: false, affectedActivityIds: [] })]
    expect(computeWeatherSummaryStats(logs).unattributedCount).toBe(0)
  })

  // domain-rules.md §8.2: a superseded/retracted entry must never contribute to the tiles.
  it('excludes superseded and retracted entries from every count', () => {
    const original = makeLog({ id: 'e1', logDate: '2026-07-08T00:00:00Z' })
    const correction = makeLog({
      id: 'e2',
      logDate: '2026-07-08T00:00:00Z',
      entryKind: 'Correction',
      correctsWeatherLogId: 'e1',
      correctionReason: 'reduced hours',
      impact: 'NoImpact',
      workStoppage: false,
      hoursLost: null,
    })

    const stats = computeWeatherSummaryStats([original, correction])
    // The correction reduced Impact to NoImpact — the superseded original's FullStoppage must not
    // leak into the stoppage count.
    expect(stats.stoppageDayCount).toBe(0)
  })

  it('returns all zeros for an empty list', () => {
    expect(computeWeatherSummaryStats([])).toEqual({ rainDayCount: 0, stoppageDayCount: 0, unattributedCount: 0 })
  })
})
