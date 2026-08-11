import { toDateInputValue, toRequestDate } from './weatherLabels'
import type { WeatherCondition, WeatherImpact, WeatherLogDto } from './types'

/** All-string/controlled form state, shared by `WeatherRecordModal` (blank start) and
 * `WeatherCorrectionModal` (pre-filled from the entry being corrected) — mirrors
 * `features/info/ProjectEditForm.tsx`'s "all-string `FormValues`, parsed only at submit" pattern. */
export interface WeatherLogFormValues {
  /** `<input type="date">` value (`"2026-07-08"`), not the wire ISO instant. */
  logDate: string
  condition: WeatherCondition
  conditionNote: string
  /** Free-typed decimal text, not a JS number — never coerced mid-typing. */
  rainfallMm: string
  impact: WeatherImpact
  impactNote: string
  hoursLost: string
  activityIds: string[]
}

/** `<input type="date">` value for "today", in the project's display timezone — the sensible
 * default for a same-day field log. */
export function todayDateInputValue(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok' }).format(new Date())
}

export function emptyWeatherLogFormValues(): WeatherLogFormValues {
  return {
    logDate: todayDateInputValue(),
    condition: 'Clear',
    conditionNote: '',
    rainfallMm: '',
    impact: 'NoImpact',
    impactNote: '',
    hoursLost: '',
    activityIds: [],
  }
}

/** Pre-fills a correction's starting values from the chain-tail entry being corrected — a
 * correction **replaces**, it does not patch (domain-rules.md §8.2 rule 6), so every field must
 * start from the target's current content for "leave everything but the typo alone" to work. */
export function weatherLogFormValuesFromEntry(entry: WeatherLogDto): WeatherLogFormValues {
  return {
    logDate: toDateInputValue(entry.logDate),
    condition: entry.condition,
    conditionNote: entry.conditionNote ?? '',
    rainfallMm: entry.rainfallMm ?? '',
    impact: entry.impact,
    impactNote: entry.impactNote ?? '',
    hoursLost: entry.hoursLost ?? '',
    activityIds: entry.affectedActivityIds,
  }
}

/** domain-rules.md §3.4: "`HoursLost` is required by FluentValidation when `Impact <> NoImpact`" —
 * mirrored here for fast client-side feedback only; `RecordWeatherLogCommandValidator`/
 * `RecordWeatherLogCorrectionCommandValidator` remain the sole authority server-side. Returns a
 * Thai error message, or `null` when the values are submittable. */
export function validateWeatherLogFormValues(values: WeatherLogFormValues): string | null {
  if (!values.logDate) return 'กรุณาระบุวันที่'

  if (values.impact !== 'NoImpact' && values.hoursLost.trim() === '') {
    return 'กรุณาระบุจำนวนชั่วโมงที่หยุดงาน (บังคับเมื่อมีผลกระทบต่องาน)'
  }
  if (values.hoursLost.trim() !== '') {
    const hours = Number(values.hoursLost)
    if (Number.isNaN(hours) || hours < 0 || hours > 24) return 'จำนวนชั่วโมงที่หยุดงานต้องเป็นตัวเลขระหว่าง 0-24'
  }
  if (values.rainfallMm.trim() !== '') {
    const rainfall = Number(values.rainfallMm)
    if (Number.isNaN(rainfall) || rainfall < 0) return 'ปริมาณฝนต้องเป็นตัวเลขและไม่ติดลบ'
  }
  return null
}

/** The wire-shape fields common to both `RecordWeatherLogPayload` and
 * `RecordWeatherLogCorrectionPayload` — empty strings become `null` (never sent as `""`), matching
 * every optional-text convention already established across this codebase (e.g.
 * `features/vo/components/VoContentModal.tsx`). */
export function buildWeatherLogRequestFields(values: WeatherLogFormValues) {
  return {
    logDate: toRequestDate(values.logDate),
    condition: values.condition,
    conditionNote: values.conditionNote.trim() === '' ? null : values.conditionNote.trim(),
    rainfallMm: values.rainfallMm.trim() === '' ? null : values.rainfallMm.trim(),
    impact: values.impact,
    impactNote: values.impactNote.trim() === '' ? null : values.impactNote.trim(),
    hoursLost: values.hoursLost.trim() === '' ? null : values.hoursLost.trim(),
    affectedActivityIds: values.activityIds,
  }
}
