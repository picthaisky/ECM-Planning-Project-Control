import { useEffect, useState } from 'react'
import { listWeatherLogs } from './api'
import type { WeatherLogOptionDto } from './types'

/**
 * Loads the project's weather logs (`GET /projects/{id}/weather-logs`) for the form's optional
 * related-weather-log dropdown. Degrades to an empty list on any failure — the form then falls back
 * to a raw-GUID text input, so logging is never blocked.
 */
export function useWeatherLogs(projectId: string): WeatherLogOptionDto[] {
  const [logs, setLogs] = useState<WeatherLogOptionDto[]>([])

  useEffect(() => {
    if (!projectId) return
    let cancelled = false
    listWeatherLogs(projectId)
      .then((loaded) => {
        if (!cancelled) setLogs(loaded)
      })
      .catch(() => {
        if (!cancelled) setLogs([])
      })
    return () => {
      cancelled = true
    }
  }, [projectId])

  return logs
}
