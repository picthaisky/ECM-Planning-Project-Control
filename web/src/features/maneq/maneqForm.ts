import { toRequestDate, todayDateInputValue } from './maneqDates'
import type { LabourType, ManpowerLogDto, RecordManpowerLogCorrectionPayload, RecordManpowerLogPayload, Shift } from './types'

/** All-string/controlled form state — mirrors `features/weather/weatherForm.ts`'s established
 * "all-string FormValues, parsed only at submit" pattern exactly. */
export interface ManpowerLogFormValues {
  logDate: string
  shift: Shift
  workCategoryId: string
  wbsNodeId: string
  activityId: string
  labourType: LabourType
  subcontractorRef: string
  workerCount: string
  manHours: string
  overtimeHours: string
  manHoursDerived: boolean
  equipmentCount: string
  equipmentOperatingHours: string
  equipmentStandbyHours: string
  workDescription: string
  relatedWeatherLogId: string
}

export function emptyManpowerLogFormValues(): ManpowerLogFormValues {
  return {
    logDate: todayDateInputValue(),
    shift: 'Day',
    workCategoryId: '',
    wbsNodeId: '',
    activityId: '',
    labourType: 'OwnDirect',
    subcontractorRef: '',
    workerCount: '',
    manHours: '',
    overtimeHours: '0',
    manHoursDerived: false,
    equipmentCount: '0',
    equipmentOperatingHours: '0',
    equipmentStandbyHours: '0',
    workDescription: '',
    relatedWeatherLogId: '',
  }
}

/** Pre-fills a correction's starting values from the chain-tail entry being corrected — a correction
 * **replaces**, it does not patch (domain-rules.md §4.7 rule 6), same discipline as
 * `features/weather/weatherForm.ts#weatherLogFormValuesFromEntry`. */
export function manpowerLogFormValuesFromEntry(entry: ManpowerLogDto): ManpowerLogFormValues {
  return {
    logDate: entry.logDate.slice(0, 10),
    shift: entry.shift,
    workCategoryId: entry.workCategoryId,
    wbsNodeId: entry.wbsNodeId ?? '',
    activityId: entry.activityId ?? '',
    labourType: entry.labourType,
    subcontractorRef: entry.subcontractorRef ?? '',
    workerCount: String(entry.workerCount),
    manHours: entry.manHours,
    overtimeHours: entry.overtimeHours,
    manHoursDerived: entry.manHoursDerived,
    equipmentCount: String(entry.equipmentCount),
    equipmentOperatingHours: entry.equipmentOperatingHours,
    equipmentStandbyHours: entry.equipmentStandbyHours,
    workDescription: entry.workDescription ?? '',
    relatedWeatherLogId: entry.relatedWeatherLogId ?? '',
  }
}

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function toGuidOrError(value: string, label: string): string | null {
  return value.trim() === '' || GUID_PATTERN.test(value.trim()) ? null : `${label} ต้องอยู่ในรูปแบบ GUID`
}

function toNonNegativeNumberOrError(value: string, label: string): string | null {
  if (value.trim() === '') return `กรุณาระบุ${label}`
  const numeric = Number(value)
  return Number.isFinite(numeric) && numeric >= 0 ? null : `${label}ต้องเป็นตัวเลขและไม่ติดลบ`
}

/**
 * Client-side mirror of `RecordManpowerLogCommandValidator`'s rules (domain-rules.md §4.1's
 * validation table) — fast feedback only; the backend validator remains the sole authority. Returns
 * a Thai error message, or `null` when the values are submittable.
 */
export function validateManpowerLogFormValues(values: ManpowerLogFormValues): string | null {
  if (!values.logDate) return 'กรุณาระบุวันที่'

  const workCategoryError = values.workCategoryId.trim() === ''
    ? 'กรุณาระบุรหัสหมวดงาน (Work Category ID)'
    : toGuidOrError(values.workCategoryId, 'รหัสหมวดงาน (Work Category ID)')
  if (workCategoryError) return workCategoryError

  for (const [value, label] of [
    [values.wbsNodeId, 'รหัส WBS Node'],
    [values.activityId, 'รหัสกิจกรรม (Activity ID)'],
    [values.relatedWeatherLogId, 'รหัสบันทึกสภาพอากาศที่เกี่ยวข้อง'],
  ] as const) {
    const error = toGuidOrError(value, label)
    if (error) return error
  }

  for (const [value, label] of [
    [values.workerCount, 'จำนวนคน'],
    [values.manHours, 'ชั่วโมงแรงงาน'],
    [values.overtimeHours, 'ชั่วโมงล่วงเวลา'],
    [values.equipmentCount, 'จำนวนเครื่องจักร'],
    [values.equipmentOperatingHours, 'ชั่วโมงทำงานเครื่องจักร'],
    [values.equipmentStandbyHours, 'ชั่วโมง Standby เครื่องจักร'],
  ] as const) {
    const error = toNonNegativeNumberOrError(value, label)
    if (error) return error
  }

  const workerCount = Number(values.workerCount)
  const manHours = Number(values.manHours)
  const overtimeHours = Number(values.overtimeHours)
  const equipmentCount = Number(values.equipmentCount)
  const equipmentOperatingHours = Number(values.equipmentOperatingHours)
  const equipmentStandbyHours = Number(values.equipmentStandbyHours)

  if (manHours > workerCount * 24) return 'ชั่วโมงแรงงานต้องไม่เกินจำนวนคน × 24 ชั่วโมง'
  if (manHours > 0 && workerCount <= 0) return 'มีชั่วโมงแรงงานแต่ไม่มีจำนวนคน — กรุณาตรวจสอบ'
  if (overtimeHours > manHours) return 'ชั่วโมงล่วงเวลาต้องไม่เกินชั่วโมงแรงงานรวม'
  if (equipmentOperatingHours + equipmentStandbyHours > equipmentCount * 24) {
    return 'ชั่วโมงทำงาน + ชั่วโมง Standby ของเครื่องจักร ต้องไม่เกินจำนวนเครื่องจักร × 24 ชั่วโมง'
  }
  if (values.subcontractorRef.length > 100) return 'ชื่อผู้รับเหมาช่วงต้องไม่เกิน 100 ตัวอักษร'
  if (values.workDescription.length > 500) return 'รายละเอียดงานต้องไม่เกิน 500 ตัวอักษร'

  return null
}

function toNullableGuid(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

/** The wire-shape fields common to both `RecordManpowerLogPayload` and
 * `RecordManpowerLogCorrectionPayload` — mirrors `features/weather/weatherForm.ts#buildWeatherLogRequestFields`. */
function buildManpowerLogRequestFields(values: ManpowerLogFormValues) {
  return {
    logDate: toRequestDate(values.logDate),
    shift: values.shift,
    workCategoryId: values.workCategoryId.trim(),
    wbsNodeId: toNullableGuid(values.wbsNodeId),
    activityId: toNullableGuid(values.activityId),
    labourType: values.labourType,
    subcontractorRef: values.subcontractorRef.trim() === '' ? null : values.subcontractorRef.trim(),
    workerCount: Number(values.workerCount),
    manHours: values.manHours.trim(),
    overtimeHours: values.overtimeHours.trim() === '' ? '0' : values.overtimeHours.trim(),
    manHoursDerived: values.manHoursDerived,
    equipmentCount: Number(values.equipmentCount || '0'),
    equipmentOperatingHours: values.equipmentOperatingHours.trim() === '' ? '0' : values.equipmentOperatingHours.trim(),
    equipmentStandbyHours: values.equipmentStandbyHours.trim() === '' ? '0' : values.equipmentStandbyHours.trim(),
    workDescription: values.workDescription.trim() === '' ? null : values.workDescription.trim(),
    relatedWeatherLogId: toNullableGuid(values.relatedWeatherLogId),
  }
}

export function buildRecordManpowerLogPayload(values: ManpowerLogFormValues, allowDuplicate = false): RecordManpowerLogPayload {
  return { ...buildManpowerLogRequestFields(values), allowDuplicate }
}

export function buildRecordManpowerLogCorrectionPayload(
  values: ManpowerLogFormValues,
  entryKind: Extract<RecordManpowerLogCorrectionPayload['entryKind'], 'Correction' | 'Retraction'>,
  correctionReason: string,
): RecordManpowerLogCorrectionPayload {
  return { ...buildManpowerLogRequestFields(values), entryKind, correctionReason }
}
