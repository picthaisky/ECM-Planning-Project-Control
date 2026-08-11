import { effectiveWeatherLogs } from './weatherChain'
import type { WeatherLogDto } from './types'

/** Conditions counted as "a rain day" for the summary tile — every rain-bearing value in the
 * closed `WeatherCondition` vocabulary (domain-rules.md §8.1). `Clear`/`Cloudy`/`Other` are
 * deliberately excluded: they carry no rain by definition, and `Other` is free-text-qualified
 * (`conditionNote`), not itself evidence of rain. */
const RAIN_CONDITIONS = new Set(['LightRain', 'ModerateRain', 'HeavyRain', 'Storm', 'Flood'])

export interface WeatherSummaryStats {
  /** Distinct calendar days, among currently-in-force entries, recorded with a rain-bearing
   * `condition` OR a positive `rainfallMm` (a depth can be logged under `Other`/`Cloudy` too — the
   * measured figure is stronger evidence than the closed-vocabulary label alone). */
  rainDayCount: number
  /** Distinct calendar days, among currently-in-force entries, whose (server-derived) `workStoppage`
   * is true — i.e. `Impact != NoImpact`. This is the same boolean the EOT evaluator's countability
   * gates start from (§3.4), though this tile counts *recorded* stoppage days, not *countable* EOT
   * days — those additionally require the §3 gates (working day, activity window, thresholds...),
   * which only a real evaluation (`EotEvaluationPanel`) can answer. */
  stoppageDayCount: number
  /** Currently-in-force entries whose stoppage names no affected activity — legitimate evidence
   * that cannot yet be evaluated (domain-rules.md §3.2, fixture W-09) until a correction adds one.
   * Surfaced so the correction path in `WeatherRecordModal`/`WeatherLogTable` has an obvious "you
   * have N of these" prompt rather than requiring the user to notice it row by row. */
  unattributedCount: number
}

/**
 * Computes the weather screen's three summary-tile numbers from the **full** (unfiltered) history —
 * this function itself narrows to the effective set (`effectiveWeatherLogs`) before counting, so a
 * superseded/retracted entry never double-counts alongside its replacement.
 */
export function computeWeatherSummaryStats(logs: readonly WeatherLogDto[]): WeatherSummaryStats {
  const effective = effectiveWeatherLogs(logs)

  const rainDates = new Set<string>()
  const stoppageDates = new Set<string>()
  let unattributedCount = 0

  for (const log of effective) {
    const dateKey = log.logDate.slice(0, 10)
    const hasRain = RAIN_CONDITIONS.has(log.condition) || (log.rainfallMm !== null && Number(log.rainfallMm) > 0)
    if (hasRain) rainDates.add(dateKey)
    if (log.workStoppage) stoppageDates.add(dateKey)
    if (log.impact !== 'NoImpact' && log.affectedActivityIds.length === 0) unattributedCount += 1
  }

  return {
    rainDayCount: rainDates.size,
    stoppageDayCount: stoppageDates.size,
    unattributedCount,
  }
}
