import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { recordWeatherLog, recordWeatherLogCorrection, WeatherApiError } from './api'
import type { RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload } from './types'

export type WeatherActionKind = 'record' | 'correct'

/**
 * Drives the two S11-BE-01 writes (`recordWeatherLog`/`recordWeatherLogCorrection`) — hand-rolled
 * busy/error state, mirroring `features/vo/useVariationOrderActions.ts`'s established pattern.
 * `onSaved` is called after either write succeeds so the caller (`WeatherPage.tsx`) can reload the
 * full list (`useWeatherLogs.ts`'s own remarks on why a reload, not a local patch, is used here).
 */
export function useWeatherLogActions(projectId: string, onSaved: () => void) {
  const [busyAction, setBusyAction] = useState<WeatherActionKind | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const clearActionError = useCallback(() => setActionError(null), [])

  const record = useCallback(
    async (payload: RecordWeatherLogPayload) => {
      setBusyAction('record')
      setActionError(null)
      try {
        const saved = await recordWeatherLog(projectId, payload)
        onSaved()
        pushToast({ message: `บันทึกสภาพอากาศวันที่ ${payload.logDate.slice(0, 10)} เรียบร้อยแล้ว (แก้ไขไม่ได้ — บันทึกรายการแก้ไขใหม่หากต้องแก้)` })
        return saved
      } catch (error) {
        setActionError(error instanceof WeatherApiError ? error.message : 'บันทึกสภาพอากาศไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [projectId, onSaved],
  )

  const correct = useCallback(
    async (logId: string, payload: RecordWeatherLogCorrectionPayload) => {
      setBusyAction('correct')
      setActionError(null)
      try {
        const saved = await recordWeatherLogCorrection(projectId, logId, payload)
        onSaved()
        pushToast({
          message:
            payload.entryKind === 'Retraction'
              ? 'บันทึกรายการเพิกถอนเรียบร้อยแล้ว — รายการเดิมไม่มีผลอีกต่อไป'
              : 'บันทึกรายการแก้ไขเรียบร้อยแล้ว — รายการเดิมยังถูกเก็บไว้เป็นหลักฐาน',
        })
        return saved
      } catch (error) {
        setActionError(error instanceof WeatherApiError ? error.message : 'บันทึกรายการแก้ไขไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [projectId, onSaved],
  )

  return { busyAction, actionError, clearActionError, record, correct }
}
