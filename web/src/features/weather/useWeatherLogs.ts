import { useCallback, useEffect, useState } from 'react'
import { listWeatherLogs, WeatherApiError } from './api'
import type { WeatherLogDto } from './types'

export type WeatherLogsLoadState = 'loading' | 'ready' | 'error'

/**
 * Loads the S11-FE-01 weather log register (`GET .../projects/{projectId}/weather-logs` — real,
 * live, full unfiltered history) — mirrors `features/vo/useVariationOrders.ts`'s established
 * load/reload shape.
 *
 * Deliberately **reloads the whole list after every mutation** (`recordWeatherLog`/
 * `recordWeatherLogCorrection` in `useWeatherLogActions.ts` both call `reload` on success) rather
 * than patching the single returned `WeatherLogDto` into local state: the correction-chain state
 * (`weatherChain.ts`) and the summary tiles (`weatherStats.ts`) are both derived from the *whole*
 * list, and re-deriving them from a server-confirmed full reload is simpler and strictly safer than
 * hand-splicing one row into a chain whose correctness depends on every other row too.
 */
export function useWeatherLogs(projectId: string) {
  const [logs, setLogs] = useState<WeatherLogDto[]>([])
  const [loadState, setLoadState] = useState<WeatherLogsLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await listWeatherLogs(projectId)
      setLogs(data)
      setLoadState('ready')
      return data
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof WeatherApiError ? error.message : 'โหลดบันทึกสภาพอากาศไม่สำเร็จ')
      return null
    }
  }, [projectId])

  useEffect(() => {
    void reload()
  }, [reload])

  return { logs, loadState, loadError, reload }
}
