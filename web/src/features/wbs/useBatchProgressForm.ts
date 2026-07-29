import { useCallback, useMemo, useState } from 'react'
import { batchRecordProgress, WbsApiError } from './api'
import type { ActivityForProgress } from './types'

export interface ProgressRow {
  activityId: string
  /** `null` for a manually-added row (`addManualRow`) — the current % is unknown until the
   * pending `getNodeActivities` endpoint exists, so decrease-detection simply cannot fire for
   * that row (there is nothing real to compare against; see `isDecreased`). */
  activityCode: string | null
  name: string | null
  currentProgressPercentage: string | null
  newProgressPercentage: string
  actualQuantity: string
}

export type BatchSubmitState = 'idle' | 'submitting' | 'error'

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function isDecreased(row: ProgressRow): boolean {
  if (row.currentProgressPercentage === null) return false
  const current = Number(row.currentProgressPercentage)
  const next = Number(row.newProgressPercentage)
  if (Number.isNaN(current) || Number.isNaN(next)) return false
  return next < current
}

/**
 * Drives the S4-FE-03 "โหมดอัปเดตความคืบหน้า" batch grid (US-4.5): row add/edit/remove, the
 * decrease-confirmation gate ("ยืนยันการปรับลดความคืบหน้า" — a row whose new % is lower than its
 * known current % blocks submission until explicitly confirmed), and the real
 * `POST .../progress/batch` call sharing one `periodEndDate` across every row in the batch.
 */
export function useBatchProgressForm(projectId: string) {
  const [rows, setRows] = useState<ProgressRow[]>([])
  const [periodEndDate, setPeriodEndDate] = useState('')
  const [validationError, setValidationError] = useState<string | null>(null)
  const [pendingConfirmation, setPendingConfirmation] = useState(false)
  const [submitState, setSubmitState] = useState<BatchSubmitState>('idle')
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [lastResultCount, setLastResultCount] = useState<number | null>(null)

  const addActivities = useCallback((activities: ActivityForProgress[]) => {
    setRows((prev) => {
      const existingIds = new Set(prev.map((r) => r.activityId))
      const additions = activities
        .filter((a) => !existingIds.has(a.id))
        .map<ProgressRow>((a) => ({
          activityId: a.id,
          activityCode: a.activityCode,
          name: a.name,
          currentProgressPercentage: a.currentProgressPercentage,
          newProgressPercentage: a.currentProgressPercentage,
          actualQuantity: '',
        }))
      return [...prev, ...additions]
    })
  }, [])

  /** Interim manual-entry path (`api.ts`'s `getNodeActivities` remarks): lets a user add a row by
   * pasting a real `ActivityId` even before the node-activities list endpoint exists. */
  const addManualRow = useCallback((activityId: string): string | null => {
    const trimmed = activityId.trim()
    if (!GUID_PATTERN.test(trimmed)) {
      return 'รหัสกิจกรรมต้องอยู่ในรูปแบบ GUID เช่น 3fa85f64-5717-4562-b3fc-2c963f66afa6'
    }

    let duplicate = false
    setRows((prev) => {
      if (prev.some((r) => r.activityId === trimmed)) {
        duplicate = true
        return prev
      }
      return [
        ...prev,
        {
          activityId: trimmed,
          activityCode: null,
          name: null,
          currentProgressPercentage: null,
          newProgressPercentage: '',
          actualQuantity: '',
        },
      ]
    })

    return duplicate ? 'กิจกรรมนี้อยู่ในรายการแล้ว' : null
  }, [])

  const updateRowProgress = useCallback((activityId: string, value: string) => {
    setRows((prev) =>
      prev.map((r) => (r.activityId === activityId ? { ...r, newProgressPercentage: value } : r)),
    )
  }, [])

  const updateRowQuantity = useCallback((activityId: string, value: string) => {
    setRows((prev) =>
      prev.map((r) => (r.activityId === activityId ? { ...r, actualQuantity: value } : r)),
    )
  }, [])

  const removeRow = useCallback((activityId: string) => {
    setRows((prev) => prev.filter((r) => r.activityId !== activityId))
  }, [])

  const decreasedRows = useMemo(() => rows.filter(isDecreased), [rows])

  const validate = useCallback((): string | null => {
    if (!periodEndDate) return 'กรุณาระบุวันที่ของงวดข้อมูล (Period End Date)'
    if (rows.length === 0) return 'กรุณาเพิ่มกิจกรรมอย่างน้อย 1 รายการ'
    for (const row of rows) {
      const numeric = Number(row.newProgressPercentage)
      if (row.newProgressPercentage.trim() === '' || Number.isNaN(numeric) || numeric < 0 || numeric > 100) {
        return `ค่า % ความคืบหน้าของกิจกรรม ${row.activityCode ?? row.activityId} ต้องอยู่ระหว่าง 0.00 ถึง 100.00`
      }
      if (row.actualQuantity.trim() !== '' && Number(row.actualQuantity) < 0) {
        return `ปริมาณงานจริงของกิจกรรม ${row.activityCode ?? row.activityId} ต้องไม่ติดลบ`
      }
    }
    return null
  }, [periodEndDate, rows])

  const performSubmit = useCallback(async () => {
    setSubmitState('submitting')
    setSubmitError(null)
    try {
      const result = await batchRecordProgress(projectId, {
        periodEndDate: new Date(`${periodEndDate}T00:00:00Z`).toISOString(),
        entries: rows.map((r) => ({
          activityId: r.activityId,
          progressPercentage: r.newProgressPercentage,
          actualQuantity: r.actualQuantity.trim() === '' ? null : r.actualQuantity,
        })),
      })
      setLastResultCount(result.entriesRecorded)
      setSubmitState('idle')
      setRows([])
      return true
    } catch (error) {
      setSubmitState('error')
      setSubmitError(error instanceof WbsApiError ? error.message : 'บันทึกความคืบหน้าไม่สำเร็จ')
      return false
    } finally {
      setPendingConfirmation(false)
    }
  }, [projectId, periodEndDate, rows])

  /** Called by the submit button. Returns `true` if the batch was sent (or the confirmation modal
   * was opened instead — check `pendingConfirmation`); `false` on a client-side validation
   * rejection (`validationError` is set). */
  const attemptSubmit = useCallback(async () => {
    const error = validate()
    setValidationError(error)
    if (error) return false

    if (decreasedRows.length > 0) {
      setPendingConfirmation(true)
      return false
    }

    return performSubmit()
  }, [decreasedRows, performSubmit, validate])

  const confirmDecreaseAndSubmit = useCallback(() => performSubmit(), [performSubmit])
  const cancelDecreaseConfirmation = useCallback(() => setPendingConfirmation(false), [])

  return {
    rows,
    periodEndDate,
    setPeriodEndDate,
    addActivities,
    addManualRow,
    updateRowProgress,
    updateRowQuantity,
    removeRow,
    decreasedRows,
    validationError,
    pendingConfirmation,
    attemptSubmit,
    confirmDecreaseAndSubmit,
    cancelDecreaseConfirmation,
    submitState,
    submitError,
    lastResultCount,
  }
}
