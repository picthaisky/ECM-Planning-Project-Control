import type { WeatherLogDto } from './types'

/**
 * Per-entry chain status, derived client-side from the full history `GET .../weather-logs` returns
 * (domain-rules.md weather-eot §8.2). The effective set is:
 *
 *   L_eff = { l in L : (no l' in L with l'.correctsWeatherLogId = l.id) AND l.entryKind != Retraction }
 *
 * i.e. an entry is in force iff nothing points at it **and** it is not itself a retraction — a
 * `Retraction` therefore removes both itself and its target from `L_eff`, which is why `inForce` is
 * computed the same way for both cases below rather than as two special cases.
 */
export type WeatherChainState = 'effective' | 'corrected' | 'retracted'

export interface WeatherLogChainInfo {
  /** True iff this entry is a member of L_eff — the only set the EOT evaluator ever reads (§8.2). */
  inForce: boolean
  /** Presentational classification of `inForce`/`entryKind` for the table's "สถานะบันทึก" column. */
  state: WeatherChainState
  /** The entry that points at this one via `correctsWeatherLogId`, if any (undefined chain link data
   * is never invented — `null` when nothing in the loaded list points at this row, including the
   * legitimate "nothing has corrected this yet" case). */
  supersededBy: WeatherLogDto | null
  /** The entry this one corrects/retracts, resolved from the same loaded list (`null` for an
   * `Original` entry, or if the target was not present in the list passed in). */
  correctsTarget: WeatherLogDto | null
  /** True iff nothing in the loaded list points at this entry yet — the chain-integrity precondition
   * (§8.2 rule 2/3) a correction's target must satisfy, mirrored client-side purely so the UI never
   * offers a "แก้ไข" action that the server would reject with 409 `WeatherLogAlreadySuperseded`. The
   * server remains the sole authority; this is a UX courtesy, not a substitute for its own check. */
  isChainTail: boolean
}

/**
 * Builds a `WeatherLogDto.id -> WeatherLogChainInfo` map for a full (unfiltered) weather-log list.
 * Pure and total: every id in `logs` gets an entry, and lookups never throw for an id absent from
 * `logs` (`correctsTarget`/`supersededBy` simply read `null` in that case).
 */
export function buildWeatherChainInfo(logs: readonly WeatherLogDto[]): Map<string, WeatherLogChainInfo> {
  const byId = new Map<string, WeatherLogDto>()
  for (const log of logs) byId.set(log.id, log)

  // §8.2 chain-integrity rule 2: "at most one entry may point at any given entry" — the backend
  // enforces this with a unique index, so at most one match is ever expected here. If the loaded
  // data somehow violated it, the first one found wins deterministically rather than throwing.
  const supersededByTarget = new Map<string, WeatherLogDto>()
  for (const log of logs) {
    if (log.correctsWeatherLogId && !supersededByTarget.has(log.correctsWeatherLogId)) {
      supersededByTarget.set(log.correctsWeatherLogId, log)
    }
  }

  const result = new Map<string, WeatherLogChainInfo>()
  for (const log of logs) {
    const supersededBy = supersededByTarget.get(log.id) ?? null
    const isChainTail = supersededBy === null
    const inForce = isChainTail && log.entryKind !== 'Retraction'

    const state: WeatherChainState = !isChainTail
      ? supersededBy!.entryKind === 'Retraction'
        ? 'retracted'
        : 'corrected'
      : log.entryKind === 'Retraction'
        ? 'retracted'
        : 'effective'

    result.set(log.id, {
      inForce,
      state,
      supersededBy,
      correctsTarget: log.correctsWeatherLogId ? (byId.get(log.correctsWeatherLogId) ?? null) : null,
      isChainTail,
    })
  }

  return result
}

/** The subset of `logs` currently in force (L_eff) — what the EOT evaluator reads and what any
 * "current state of the weather record" summary (tiles, stats) should be computed from, never the
 * raw, unfiltered history (which still includes superseded/retracted rows for audit purposes). */
export function effectiveWeatherLogs(logs: readonly WeatherLogDto[]): WeatherLogDto[] {
  const chain = buildWeatherChainInfo(logs)
  return logs.filter((log) => chain.get(log.id)?.inForce ?? false)
}
