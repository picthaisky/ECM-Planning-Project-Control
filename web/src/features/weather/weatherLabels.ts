import type {
  EotConfidence,
  EotCriticalityBasis,
  EotExclusionReason,
  WeatherCondition,
  WeatherImpact,
  WeatherLogEntryKind,
} from './types'

/** Thai display text for `WeatherCondition` (domain-rules.md §8.1's closed vocabulary). Colloquial
 * enough to match the prototype's own wording ("ฝนปรอยๆ", "ฝนตกหนัก") while still covering every
 * backend enum member — a value not in this list is a defect, not an expected fallback. */
export const WEATHER_CONDITION_LABELS: Record<WeatherCondition, string> = {
  Clear: 'ท้องฟ้าแจ่มใส',
  Cloudy: 'มีเมฆมาก / ครึ้มฟ้า',
  LightRain: 'ฝนตกเล็กน้อย (ปรอยๆ)',
  ModerateRain: 'ฝนตกปานกลาง',
  HeavyRain: 'ฝนตกหนัก',
  Storm: 'พายุฝนฟ้าคะนอง',
  Flood: 'น้ำท่วม',
  Other: 'อื่นๆ',
}

/** Thai display text for `WeatherImpact` — `WorkStoppage` is derived from this server-side (§3.4),
 * never edited independently, so this is the one field that decides the table's "หยุดงาน" pill. */
export const WEATHER_IMPACT_LABELS: Record<WeatherImpact, string> = {
  NoImpact: 'ไม่กระทบงาน',
  PartialStoppage: 'หยุดงานบางส่วน',
  FullStoppage: 'หยุดงานทั้งวัน',
}

/** Thai display text for `WeatherLogEntryKind` (§8.2's correction chain). */
export const WEATHER_ENTRY_KIND_LABELS: Record<WeatherLogEntryKind, string> = {
  Original: 'ต้นฉบับ',
  Correction: 'รายการแก้ไข',
  Retraction: 'รายการเพิกถอน',
}

/** Thai display text for `EotCriticalityBasis` (§4.4's degraded-mode ladder) — kept short for a
 * badge, with the full explanation in `EOT_CRITICALITY_BASIS_EXPLANATIONS`. */
export const EOT_CRITICALITY_BASIS_LABELS: Record<EotCriticalityBasis, string> = {
  Contemporaneous: 'ตามตารางเวลา ณ วันที่เกิดเหตุ',
  Mixed: 'ผสม (มี/ไม่มีตารางเวลา ณ ขณะนั้น)',
  Retrospective: 'ย้อนหลังทั้งหมด',
}

/** One sentence per `EotCriticalityBasis`, for the evaluation panel's explainability requirement
 * (DoD: "the answer be explainable") — domain-rules.md §4.1/§4.4. */
export const EOT_CRITICALITY_BASIS_EXPLANATIONS: Record<EotCriticalityBasis, string> = {
  Contemporaneous:
    'ทุกวันที่นับได้มีผลคำนวณตารางเวลา (CPM) ที่คำนวณไว้ก่อนหรือในวันนั้นจริง — ใช้ความสำคัญ (Critical) ของตารางเวลา ณ วันที่เกิดเหตุ ไม่ใช่ตารางเวลาปัจจุบัน',
  Mixed:
    'บางวันมีผลคำนวณตารางเวลา ณ ขณะนั้น บางวันไม่มี (ใช้ตารางเวลาแรกสุดที่มีแทนสำหรับวันที่ไม่มีข้อมูล) — ผลลัพธ์บางส่วนเป็นการประเมินเบื้องต้น',
  Retrospective:
    'ไม่มีผลคำนวณตารางเวลาก่อนหรือในวันที่เกิดเหตุแม้แต่วันเดียว จึงใช้ตารางเวลาแรกสุดที่มีอยู่แทนทั้งหมด — ผลลัพธ์เป็นการประเมินเบื้องต้นทั้งหมด',
}

/** Thai display text for `EotConfidence`. */
export const EOT_CONFIDENCE_LABELS: Record<EotConfidence, string> = {
  Substantiated: 'มีหลักฐานสนับสนุนครบถ้วน (Substantiated)',
  Provisional: 'ประเมินเบื้องต้น ยังไม่สมบูรณ์ (Provisional)',
}

/** Thai display text for `EotExclusionReason` (§3's countability gates) — used in the evaluation
 * panel's source/driver breakdown so an excluded day is explained, never silently dropped
 * (fixture W-09's own rule: "the evaluator never silently drops a row"). */
export const EOT_EXCLUSION_REASON_LABELS: Record<EotExclusionReason, string> = {
  NoAffectedActivity: 'ไม่ได้ระบุกิจกรรมที่ได้รับผลกระทบ',
  NonWorkingDay: 'ไม่ใช่วันทำงานตามปฏิทินโครงการ',
  CalendarHoliday: 'เป็นวันหยุดตามปฏิทินโครงการ',
  BelowHoursThreshold: 'จำนวนชั่วโมงที่หยุดงานไม่ถึงเกณฑ์ที่ตั้งค่าไว้',
  BelowRainfallThreshold: 'ปริมาณฝนไม่ถึงเกณฑ์ที่ตั้งค่าไว้',
  RainfallNotMeasured: 'ไม่มีการวัดปริมาณฝนไว้ (มีเกณฑ์ปริมาณฝนกำหนดไว้)',
  ActivityAlreadyComplete: 'กิจกรรมนี้เสร็จสิ้นไปแล้วก่อนวันที่บันทึก',
  ActivityNotYetScheduled: 'ยังไม่ถึงกำหนดเริ่มกิจกรรมนี้',
  NoStoppageRecorded: 'บันทึกไว้ว่าไม่มีผลกระทบต่องาน',
}

const DATE_FORMATTER = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeZone: 'Asia/Bangkok' })
const DATE_TIME_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

/** `"2026-07-08T00:00:00+07:00"` -> `"8 ก.ค. 2569"` — matches the prototype's own date format
 * exactly (`docs/ECM Planning Prototype.dc.html`'s weather table, e.g. `"8 ก.ค. 2569"`). */
export function formatWeatherDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_FORMATTER.format(parsed)
}

/** `"2026-07-08T09:15:00+07:00"` -> `"8 ก.ค. 2569 09:15"` — for timestamps where the time matters
 * (`recordedAt`, `evaluatedAt`, `closedAt`). */
export function formatWeatherDateTime(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_TIME_FORMATTER.format(parsed)
}

/** `<input type="date">` value (`"2026-07-08"`) -> UTC-midnight ISO instant, matching
 * `features/info/ProjectEditForm.tsx#toRequestDate`'s established convention exactly: a weather
 * log's `LogDate` is a calendar-day identity, not a specific instant (domain-rules.md §1's "Log
 * date — a calendar day, not an instant"), so UTC midnight is the right round-trip. */
export function toRequestDate(dateInputValue: string): string {
  return new Date(`${dateInputValue}T00:00:00Z`).toISOString()
}

const DATE_INPUT_VALUE_FORMATTER = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok' })

/**
 * ISO instant -> `<input type="date">` value, the inverse of `toRequestDate`.
 *
 * Deliberately reads the calendar day in **Bangkok time**, not a bare UTC slice
 * (`ProjectEditForm.tsx#toDateInputValue`'s simpler approach, which this function does not copy):
 * `toRequestDate` writes UTC midnight, and UTC midnight of day D is still day D in Bangkok
 * (UTC+7) — so a value this app itself produced round-trips correctly either way — but a `LogDate`
 * that arrives with a **different** offset (e.g. `+07:00`, plausible for a `DateTimeOffset` column
 * that preserves whatever offset it was written with) is where a plain `.toISOString().slice(0, 10)`
 * silently steps back a calendar day for any log timestamped before 07:00 local time. Reading in
 * `Asia/Bangkok` — the same zone every other display in this app already uses
 * (`formatWeatherDate`/`formatWeatherDateTime` above, `features/info/ProjectMasterCard.tsx`) —
 * recovers the intended calendar day regardless of which offset the value carries.
 */
export function toDateInputValue(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? '' : DATE_INPUT_VALUE_FORMATTER.format(parsed)
}
