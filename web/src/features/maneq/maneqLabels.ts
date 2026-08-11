import type { LabourType, ManpowerLogEntryKind, PiDataQualityWarning, PiNullReason, Shift } from './types'

export const SHIFT_LABELS: Record<Shift, string> = {
  Day: 'กะกลางวัน',
  Night: 'กะกลางคืน',
}

export const LABOUR_TYPE_LABELS: Record<LabourType, string> = {
  OwnDirect: 'แรงงานตรง (บริษัท)',
  Subcontract: 'ผู้รับเหมาช่วง',
  Hired: 'จ้างเหมาแรงงาน',
}

export const MANPOWER_ENTRY_KIND_LABELS: Record<ManpowerLogEntryKind, string> = {
  Original: 'ต้นฉบับ',
  Correction: 'รายการแก้ไข',
  Retraction: 'รายการเพิกถอน',
}

/**
 * domain-rules.md §5.4/§5.7's ruling, and this task's own explicit instruction: "Render '—' with the
 * *reason* in Thai, not a bare dash". Every member here must give a PM something actionable, not just
 * confirm that a number is missing.
 */
export const PI_NULL_REASON_LABELS: Record<PiNullReason, string> = {
  NotReported: 'ยังไม่มีการบันทึกกำลังคนในช่วงเวลานี้',
  NoActualManHours: 'มีบันทึกแล้วแต่ชั่วโมงแรงงานที่บันทึกไว้รวมเป็นศูนย์',
  NoBudgetManHours: 'ยังไม่ได้ประมาณการเป็นชั่วโมง-คน (Budget Man-Hours) สำหรับขอบเขตนี้',
  NoProgressInPeriod: 'ยังไม่มีข้อมูลความก้าวหน้าของงานในช่วงเวลานี้ (รอบรายงานความก้าวหน้าไม่ตรงกับช่วงที่เลือก)',
  NoMatchingBudgetedScope: 'ไม่พบกิจกรรมที่มีงบชั่วโมง-คนตรงกับขอบเขตที่เลือก',
  ActivitiesNotCategorised: 'กิจกรรมในขอบเขตนี้ยังไม่ได้จัดหมวดงาน จึงยังคำนวณ PI แยกตามหมวดงานไม่ได้',
}

export const PI_WARNING_LABELS: Record<PiDataQualityWarning, string> = {
  ImplausiblePi: 'ค่า PI อยู่นอกช่วงปกติ (0.20–3.00) — ควรตรวจสอบข้อมูลอีกครั้ง (ไม่ได้ถูกปรับค่าหรือซ่อนไว้)',
  ProgressWithoutManHours: 'มีการรายงานความก้าวหน้า แต่ไม่มีบันทึกชั่วโมงแรงงานในช่วงเวลาเดียวกัน',
  UnbudgetedLabourHours: 'มีชั่วโมงแรงงานเกิดขึ้นในขอบเขตที่กำหนดงบไว้ที่ 0 อย่างชัดเจน',
  NegativeEarnedHours: 'ชั่วโมงที่ควรได้รับติดลบ (มีการแก้ไขความก้าวหน้าย้อนหลังลดลง) — ไม่ถูกปรับให้เป็นศูนย์',
  CircularEarningBasisRisk: 'ความก้าวหน้าอาจคำนวณจากสัดส่วนชั่วโมงแรงงานเอง ทำให้ PI ใกล้ 1.00 โดยไม่สะท้อนประสิทธิภาพจริง',
}

const HOURS_FORMATTER = new Intl.NumberFormat('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const COUNT_FORMATTER = new Intl.NumberFormat('th-TH', { maximumFractionDigits: 0 })

/** `1234.5` -> `"1,234.50"` — man-hours are `decimal(9,2)` (domain-rules.md's precision rule),
 * displayed with the same 2-decimal/thousand-separator convention `utils/format.ts#formatMoney`
 * uses for money, kept as a distinct function so a reader never mistakes an hours figure for a money
 * figure just because the formatting code path looks identical to `formatMoney`. */
export function formatHours(value: number | string): string {
  const numeric = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numeric)) return '—'
  return HOURS_FORMATTER.format(numeric)
}

/** `186` -> `"186"` — plain integer headcount, thousand-separated for a very large site. */
export function formatWorkerCount(value: number): string {
  return COUNT_FORMATTER.format(value)
}

const DATE_FORMATTER = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeZone: 'Asia/Bangkok' })
const DATE_TIME_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})
const SHORT_DAY_FORMATTER = new Intl.DateTimeFormat('th-TH', { day: 'numeric', month: 'short', timeZone: 'Asia/Bangkok' })

export function formatManpowerDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_FORMATTER.format(parsed)
}

export function formatManpowerDateTime(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_TIME_FORMATTER.format(parsed)
}

/** `"2026-07-08"` -> `"8 ก.ค."` — the histogram's compact x-axis label. */
export function formatShortDay(dateInputValue: string): string {
  const parsed = new Date(`${dateInputValue}T00:00:00.000Z`)
  return Number.isNaN(parsed.getTime()) ? dateInputValue : SHORT_DAY_FORMATTER.format(parsed)
}
