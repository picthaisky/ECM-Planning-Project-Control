import type { CashFlowReceiptsUnavailableReason } from './types'

/** Stable warning codes this screen's `warnings[]` can carry: `CashFlowWarningCodes.PeriodRestated`
 * plus whatever the EVM engine itself contributes (`EvmWarningCodes`,
 * `GetCashFlowQueryHandler` appends to, never replaces, that list). Unmapped codes still render
 * (never dropped) — see `describeWarning`. */
const CASH_FLOW_WARNING_LABELS: Record<string, string> = {
  CashFlowPeriodRestated:
    'งวดที่ปิดไปแล้วมีตัวเลขไม่ตรงกับค่าล่าสุด (Restated) เนื่องจากมีการบันทึกต้นทุนหรือความคืบหน้าย้อนหลังหลังจากปิดงวดนั้น — ตัวเลขสะสมด้านบนของหน้านี้เป็นค่าล่าสุดเสมอ ส่วนแท่งกราฟของงวดที่ปิดแล้วยังคงค่า ณ วันที่ปิดงวดเดิมไว้ (ADR-0009)',
  EarnedValueExceedsBudget:
    'มูลค่างานที่ทำได้ (EV) เกินงบประมาณ (BAC) — ตรวจสอบความคืบหน้าหรือ Weight ที่บันทึกไว้อีกครั้ง',
  ActualCostIsNegative:
    'ยอดต้นทุนจริงสะสม (AC) ติดลบ (มีรายการปรับปรุง/กลับรายการมากกว่าที่บันทึกไว้) — ตัวเลขในหน้านี้ควรใช้ด้วยความระมัดระวัง',
}

/** Never hides an unmapped code — worst case shows the raw backend code. */
export function describeWarning(code: string): string {
  return CASH_FLOW_WARNING_LABELS[code] ?? code
}

const RECEIPTS_UNAVAILABLE_LABELS: Record<CashFlowReceiptsUnavailableReason, string> = {
  PaymentCertificatesNotYetImplemented:
    'ยังไม่มีระบบใบรับรองผลงาน (Payment Certificate) ในโครงการนี้ — ฟีเจอร์นี้จะเปิดใช้งานใน Sprint 9',
}

/** Thai copy for `CashFlowReceiptsDto.unavailableReason` — mirrors the `EacNullReason` "always
 * explain why, never a bare null" discipline. `reason === null` (unreachable while
 * `isAvailable === false`, but handled defensively — see `CashFlowReceiptsDto`'s own remarks) still
 * gets an honest generic line rather than crashing/showing nothing. */
export function describeReceiptsUnavailable(reason: CashFlowReceiptsUnavailableReason | null): string {
  if (reason === null) return 'ยังไม่มีข้อมูลรับเงินสำหรับโครงการนี้'
  return RECEIPTS_UNAVAILABLE_LABELS[reason]
}

/**
 * actual-cost.md §7.6's edge-case table draws a hard distinction between "no cost recorded at all"
 * (`entryCount === 0`) and "cost entries exist but net to exactly zero" (`entryCount > 0`, amount ===
 * 0.00 — e.g. an accrual fully reversed before its invoice posted) — fixture AC-6 is explicit these
 * must read differently ("ต้นทุนสุทธิเป็นศูนย์จาก 2 รายการ" ≠ "ยังไม่มีการบันทึกต้นทุน"). This is why
 * `CashFlowResponseDto` carries `actualCostEntryCount` alongside the amount — never format the AC
 * figure without also reading this field.
 */
export function describeActualCostEntryCount(acCumulative: string, entryCount: number): string {
  if (entryCount === 0) {
    return 'ยังไม่มีการบันทึกต้นทุน (Actual Cost)'
  }

  const amount = Number(acCumulative)
  if (Number.isFinite(amount) && amount === 0) {
    return `ต้นทุนสุทธิเป็นศูนย์จาก ${entryCount.toLocaleString('th-TH')} รายการ`
  }

  return `รวม ${entryCount.toLocaleString('th-TH')} รายการ (ActualCostEntry)`
}

export type MetricTone = 'neutral' | 'success' | 'danger'

/** `value >= 0` -> favourable (success/green); `< 0` -> unfavourable (danger/red); `null` -> neutral.
 * Used for Net Cash Position — matches the prototype's own literal color (`#B23A3A` danger on its
 * `−14.7 MB` mock value) and `features/evm/evmSelectors.ts#toneForSign`'s identical rule for CV/SV/
 * VAC (a fund­ing-position number colored by its own sign, never by which ledger produced it). */
export function toneForSign(value: string | null): MetricTone {
  if (value === null) return 'neutral'
  const parsed = Number(value)
  if (!Number.isFinite(parsed)) return 'neutral'
  return parsed >= 0 ? 'success' : 'danger'
}
