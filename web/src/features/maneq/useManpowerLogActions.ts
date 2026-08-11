import { useCallback, useState } from 'react'
import { pushToast } from '../../store/toastStore'
import { ManpowerApiError, recordManpowerLog, recordManpowerLogCorrection } from './api'
import type { RecordManpowerLogCorrectionPayload, RecordManpowerLogPayload } from './types'

export type ManpowerActionKind = 'record' | 'correct'

/** Drives the two S12-BE-02 writes — mirrors `features/weather/useWeatherLogActions.ts`'s established
 * busy/error-state shape exactly. `onSaved` is called after either write succeeds so the caller
 * (`ManeqPage.tsx`) can append the freshly-recorded row to its session-local table and re-fetch the
 * PI/histogram tiles (there is no `GET` list endpoint to "reload the whole register" from — see
 * `ManeqPage.tsx`'s own remarks on that gap). */
export function useManpowerLogActions(projectId: string, onSaved: () => void) {
  const [busyAction, setBusyAction] = useState<ManpowerActionKind | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const clearActionError = useCallback(() => setActionError(null), [])

  const record = useCallback(
    async (payload: RecordManpowerLogPayload) => {
      setBusyAction('record')
      setActionError(null)
      try {
        const saved = await recordManpowerLog(projectId, payload)
        onSaved()
        pushToast({ message: `บันทึกกำลังคน/เครื่องจักรวันที่ ${payload.logDate.slice(0, 10)} เรียบร้อยแล้ว` })
        return saved
      } catch (error) {
        setActionError(error instanceof ManpowerApiError ? error.message : 'บันทึกข้อมูลไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [projectId, onSaved],
  )

  const correct = useCallback(
    async (logId: string, payload: RecordManpowerLogCorrectionPayload) => {
      setBusyAction('correct')
      setActionError(null)
      try {
        const saved = await recordManpowerLogCorrection(projectId, logId, payload)
        onSaved()
        pushToast({
          message:
            payload.entryKind === 'Retraction'
              ? 'บันทึกรายการเพิกถอนเรียบร้อยแล้ว — รายการเดิมไม่มีผลอีกต่อไป'
              : 'บันทึกรายการแก้ไขเรียบร้อยแล้ว — รายการเดิมยังถูกเก็บไว้เป็นหลักฐาน',
        })
        return saved
      } catch (error) {
        setActionError(error instanceof ManpowerApiError ? error.message : 'บันทึกรายการแก้ไขไม่สำเร็จ')
        return null
      } finally {
        setBusyAction(null)
      }
    },
    [projectId, onSaved],
  )

  return { busyAction, actionError, clearActionError, record, correct }
}
