import { useCallback, useEffect, useState } from 'react'
import { ManpowerApiError, getProductivityIndex } from './api'
import { cumulativeBucketRequest, dayBucketRequest, lastNDaysDateInputValues, monthToDateBucketRequest, todayDateInputValue } from './maneqDates'
import type { ProductivityIndexResponseDto } from './types'

export type LoadState = 'loading' | 'ready' | 'error'

export interface SectionState<T> {
  state: LoadState
  error: string | null
  data: T | null
}

const LOADING_SECTION = { state: 'loading', error: null, data: null } as const

export interface HistogramPoint {
  dateInputValue: string
  response: ProductivityIndexResponseDto
}

const DEFAULT_HISTOGRAM_DAYS = 7

/**
 * Fetches the S12-FE-02 screen's real data from the **one** read endpoint this Sprint 12 backend
 * exposes for the feature — `GET .../manpower-logs/productivity-index` (`api.ts`'s own remarks on
 * why there is no list/histogram-dedicated endpoint to call instead). Four independent queries
 * against it:
 *
 * 1. **cumulative** — `from` omitted, `to` = today -> the KPI tile's $PI^{cum}(t)$ (§5.2's headline).
 * 2. **today** — an exactly-one-calendar-day bucket -> the only shape that also returns
 *    `manningRatio`/`actualWorkerCount`/`plannedWorkerCount` (`GetProductivityIndexQueryHandler.cs`'s
 *    own gate), i.e. today's "กำลังคนวันนี้" tile.
 * 3. **monthToDate** — month-start (exclusive) through today (inclusive) -> "ชม.ทำงานสะสม (เดือนนี้)".
 * 4. **histogram** — one request per day for the last `histogramDays` days, each an exactly-one-day
 *    bucket (so each also carries real per-day manning data) -> the histogram + PI-trend chart. This
 *    is real, backend-computed data per day, at the cost of N requests instead of one bulk endpoint —
 *    an honest consequence of the backend gap, not a fabricated series.
 *
 * Every section loads/fails independently (a histogram-day failure does not blank the KPI tiles).
 */
export function useManpowerOverview(projectId: string, wbsNodeId: string | null = null, histogramDays = DEFAULT_HISTOGRAM_DAYS) {
  const [cumulative, setCumulative] = useState<SectionState<ProductivityIndexResponseDto>>(LOADING_SECTION)
  const [today, setToday] = useState<SectionState<ProductivityIndexResponseDto>>(LOADING_SECTION)
  const [monthToDate, setMonthToDate] = useState<SectionState<ProductivityIndexResponseDto>>(LOADING_SECTION)
  const [histogram, setHistogram] = useState<SectionState<HistogramPoint[]>>(LOADING_SECTION)

  const reload = useCallback(async () => {
    const todayInput = todayDateInputValue()
    const scope = { wbsNodeId: wbsNodeId ?? undefined, activityId: undefined }

    setCumulative(LOADING_SECTION)
    setToday(LOADING_SECTION)
    setMonthToDate(LOADING_SECTION)
    setHistogram(LOADING_SECTION)

    await Promise.all([
      (async () => {
        try {
          const bucket = cumulativeBucketRequest(todayInput)
          const data = await getProductivityIndex(projectId, { ...scope, from: bucket.from, to: bucket.to })
          setCumulative({ state: 'ready', error: null, data })
        } catch (error) {
          setCumulative({ state: 'error', error: error instanceof ManpowerApiError ? error.message : 'โหลด Productivity Index ไม่สำเร็จ', data: null })
        }
      })(),
      (async () => {
        try {
          const bucket = dayBucketRequest(todayInput)
          const data = await getProductivityIndex(projectId, { ...scope, from: bucket.from, to: bucket.to })
          setToday({ state: 'ready', error: null, data })
        } catch (error) {
          setToday({ state: 'error', error: error instanceof ManpowerApiError ? error.message : 'โหลดข้อมูลกำลังคนวันนี้ไม่สำเร็จ', data: null })
        }
      })(),
      (async () => {
        try {
          const bucket = monthToDateBucketRequest(todayInput)
          const data = await getProductivityIndex(projectId, { ...scope, from: bucket.from, to: bucket.to })
          setMonthToDate({ state: 'ready', error: null, data })
        } catch (error) {
          setMonthToDate({ state: 'error', error: error instanceof ManpowerApiError ? error.message : 'โหลดชั่วโมงทำงานสะสมไม่สำเร็จ', data: null })
        }
      })(),
      (async () => {
        try {
          const days = lastNDaysDateInputValues(todayInput, histogramDays)
          const points = await Promise.all(
            days.map(async (dateInputValue) => {
              const bucket = dayBucketRequest(dateInputValue)
              const response = await getProductivityIndex(projectId, { ...scope, from: bucket.from, to: bucket.to })
              return { dateInputValue, response }
            }),
          )
          setHistogram({ state: 'ready', error: null, data: points })
        } catch (error) {
          setHistogram({ state: 'error', error: error instanceof ManpowerApiError ? error.message : 'โหลดข้อมูล Histogram ไม่สำเร็จ', data: null })
        }
      })(),
    ])
  }, [projectId, wbsNodeId, histogramDays])

  useEffect(() => {
    void reload()
  }, [reload])

  return { cumulative, today, monthToDate, histogram, reload }
}
