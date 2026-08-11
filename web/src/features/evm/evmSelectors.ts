import type {
  EacNullReason,
  EacVariant,
  EacVariantResponseDto,
  EvmResponseDto,
  EvmSnapshotDto,
  SCurvePoint,
} from './types'

/**
 * ADR-0007(d): the engine computes all five EAC variants. Sprint 7's UI (v1) surfaced only the
 * three index-based variants; **Sprint 14 (S14-FE-02) opens the remaining two**, `BottomUpEtc`/
 * `CustomPf` — their input UI (`EacAdvancedInputsCard`, `web/src/features/info/`) now exists, so
 * this constant grows from 3 to 5. Every component that renders a *choosable* variant list must
 * build it from this constant rather than iterating `EvmResponseDto.variants` directly, so the
 * display order/membership lives in one place, not re-decided ad hoc at each call site.
 * `BottomUpEtc`/`CustomPf` are listed last (index-based variants first, matching evm-formulas.md's
 * own table order) — `EacVariantSelect` is responsible for disabling either one until its own
 * project-level input is actually configured (S14-FE-02 DoD).
 */
export const UI_EAC_VARIANTS: readonly EacVariant[] = [
  'CpiBased',
  'Atypical',
  'CpiSpiBased',
  'BottomUpEtc',
  'CustomPf',
]

/** Short tile/option label per variant (technical terms kept in English per CLAUDE.md). */
export const VARIANT_SHORT_LABELS: Record<EacVariant, string> = {
  CpiBased: 'CPI-Based',
  Atypical: 'Atypical',
  CpiSpiBased: 'CPI × SPI',
  BottomUpEtc: 'Bottom-Up ETC',
  CustomPf: 'Custom PF',
}

/** "When to use" hint, transcribed from evm-formulas.md's own "Assumption — when to use" column. */
export const VARIANT_ASSUMPTION_LABELS: Record<EacVariant, string> = {
  CpiBased: 'อัตราต้นทุนปัจจุบันเป็นปกติและจะดำเนินต่อไปในลักษณะเดิม',
  Atypical: 'ผลต่างที่ผ่านมาเป็นเหตุการณ์ครั้งเดียว มีสาเหตุชัดเจน ไม่เกิดซ้ำ',
  CpiSpiBased: 'ความล่าช้าของตารางเวลาเองก็เป็นตัวขับต้นทุนงานที่เหลือ (เร่งงาน/OT)',
  BottomUpEtc: 'มีการประมาณการงานที่เหลือใหม่ (revised BoQ / QS re-measure)',
  CustomPf: 'กำหนดตัวคูณผลการดำเนินงานเอง (เช่น ตามเงื่อนไขที่ lender กำหนด)',
}

/** Formula caption per variant, shown under the EAC tile so the currently-selected assumption is
 * always spelled out (matches the prototype's per-tile formula sub-line convention). */
export const VARIANT_FORMULA_LABELS: Record<EacVariant, string> = {
  CpiBased: 'BAC / CPI',
  Atypical: 'AC + (BAC − EV)',
  CpiSpiBased: 'AC + (BAC − EV) / (CPI × SPI)',
  BottomUpEtc: 'AC + ETC (ประมาณการเอง)',
  CustomPf: 'AC + PF × (BAC − EV)',
}

/** Thai translation of every `EacNullReason` the backend can return — surfaced next to a
 * non-computable variant so the UI is honest about *why*, using the backend's own reason code
 * rather than the frontend re-deriving/guessing one (never duplicate the edge-case rules from
 * `evm-formulas.md` — only translate the code it already returns). */
export const EAC_NULL_REASON_LABELS: Record<EacNullReason, string> = {
  NotStarted: 'โครงการยังไม่เริ่มดำเนินการ — PV, EV และ AC ยังเป็น 0 ทั้งหมด',
  NoActualCost: 'ยังไม่มีข้อมูลค่าใช้จ่ายจริง (Actual Cost) ของโครงการนี้',
  NoPlannedValue: 'โครงการนี้ยังไม่มีเส้นฐาน (Baseline) จึงไม่มีมูลค่าตามแผน (PV)',
  ZeroCpi: 'มีค่าใช้จ่ายจริงเกิดขึ้นแล้วแต่ยังไม่มีผลงานที่ทำได้ (EV) จึงคำนวณ CPI ไม่ได้',
  ManualEtcNotSet: 'ยังไม่ได้กรอกประมาณการงานที่เหลือ (Bottom-Up ETC) — กรอกได้ที่หน้าข้อมูลโครงการ (Project Info)',
  CustomPfNotSet: 'ยังไม่ได้กำหนดตัวคูณผลการดำเนินงานเอง (Custom Performance Factor) — กำหนดได้ที่หน้าข้อมูลโครงการ (Project Info)',
}

/**
 * S14-FE-02 DoD: "`BottomUpEtc`/`CustomPf` are selectable only when their input exists; when
 * absent, the option is disabled with an explanation of where to set it." The two reasons below
 * are the only ones `EacCalculator` can return for these two variants outside two edge rules that
 * apply uniformly to *every* variant regardless of input state (`NotStarted`, and the `BAC-EV=0`
 * short-circuit, which returns `computable: true` unconditionally — see `EacCalculator.ComputeAll`'s
 * own remarks) — so a `reason` of exactly one of these two is the unambiguous "the input was never
 * configured" signal `EacVariantSelect` disables on. A `computable: true` result is always safe to
 * enable (the input necessarily exists in the only regime where these two ever fail to reach that
 * short-circuit); the reverse (a stale "false" reading while typed input truly exists) cannot
 * happen for these two variants outside `NotStarted`, which itself already renders "—" everywhere
 * else in this UI and is rare enough in practice that a temporarily-enabled-but-unselectable option
 * is an acceptable, honestly-labelled edge case rather than one worth a second network round trip
 * to resolve definitively.
 */
export const MISSING_EAC_INPUT_REASON: Partial<Record<EacVariant, EacNullReason>> = {
  BottomUpEtc: 'ManualEtcNotSet',
  CustomPf: 'CustomPfNotSet',
}

/** `EvmWarningCodes` (backend), translated. `EvmResponseDto.warnings` carries the bare code
 * strings — this was previously rendered verbatim in English (`EvmPage.tsx` used to
 * `evm.warnings.join(' · ')` with no translation at all); every known code gets a Thai message
 * here, and an unrecognised one still degrades to the raw code rather than disappearing silently
 * (never a blank warning banner for a code this table has not caught up with yet). */
export const EVM_WARNING_LABELS: Record<string, string> = {
  EarnedValueExceedsBudget:
    'มูลค่างานที่ทำได้จริง (EV) มากกว่างบประมาณ (BAC) — ตรวจสอบเปอร์เซ็นต์ความคืบหน้า/น้ำหนักงานอีกครั้ง',
  ActualCostIsNegative:
    'ค่าใช้จ่ายจริง (AC) ติดลบ (มีการกลับรายการ/เครดิตโน้ตมากกว่ายอดที่บันทึกไว้) — ตัวเลขที่อิง CPI ในหน้านี้ควรใช้ด้วยความระมัดระวัง',
  // domain-rules.md §5.7 / EvmWarningCodes.ManualEtcPredatesBacChange's own remarks: re-entering
  // `EacManualEtc` (the S14-FE-02 input, `web/src/features/info/EacAdvancedInputsCard.tsx`) is
  // exactly the fix — `EvmPage.tsx` links straight there when this code is present.
  ManualEtcPredatesBacChange:
    'มี Variation Order ที่อนุมัติแล้วเปลี่ยนงบประมาณ (BAC) หลังจากกรอกค่าประมาณการ Bottom-Up ETC ครั้งล่าสุด — ตัวเลข EAC ของ Bottom-Up ETC อาจไม่ทันปัจจุบันแล้ว',
}

/**
 * S7-FE-04 (ADR-0009): assembles `SCurveChart`'s `points` array — every closed
 * `EvmPeriodSnapshot` strictly *before* the live data date, ascending, followed by the live
 * `GET .../evm` reading as the last point. `snapshots` need not already be sorted/filtered by the
 * caller (defensive: `listEvmSnapshots` documents ascending order, but this never trusts that
 * blindly for a correctness-critical chart).
 *
 * A snapshot dated on/after the live data date is deliberately excluded, not merely de-duplicated —
 * if a period was somehow closed for a date at or beyond the project's own current data date, the
 * live reading is definitionally the more current source for that same instant, and the whole
 * point of ADR-0009 is that *past* dates must cite a frozen snapshot, not that every snapshot ever
 * closed must be plotted regardless of whether it is actually in the past.
 */
export function buildSCurvePoints(evm: Pick<EvmResponseDto, 'dataDate' | 'pv' | 'ev' | 'ac'>, snapshots: readonly EvmSnapshotDto[]): SCurvePoint[] {
  const currentTime = new Date(evm.dataDate).getTime()

  const historical = snapshots
    .filter((snapshot) => new Date(snapshot.dataDate).getTime() < currentTime)
    .slice()
    .sort((a, b) => new Date(a.dataDate).getTime() - new Date(b.dataDate).getTime())
    .map((snapshot) => ({ dataDate: snapshot.dataDate, pv: snapshot.pv, ev: snapshot.ev, ac: snapshot.ac }))

  return [...historical, { dataDate: evm.dataDate, pv: evm.pv, ev: evm.ev, ac: evm.ac }]
}

/** Finds one variant's result entry in an `EvmResponseDto.variants[]` array. `undefined` should
 * not occur in practice for any of `UI_EAC_VARIANTS` (the engine always returns all 5), but every
 * caller still treats it as optional — never assume the array shape defensively skipped here. */
export function findVariantResult(
  evm: Pick<EvmResponseDto, 'variants'>,
  variant: EacVariant,
): EacVariantResponseDto | undefined {
  return evm.variants.find((entry) => entry.variant === variant)
}

/**
 * evm-formulas.md "TCPI follows the selected variant": show `TCPI_BAC` while the forecast is still
 * within budget, switch to `TCPI_EAC` once the *currently selected* variant's own VAC has gone
 * negative (`VAC = BAC - EAC`, so `VAC < 0` means the forecast has overrun). Falls back to
 * `TCPI_BAC` whenever VAC is unknown/non-negative/`tcpiEac` itself is null — this never recomputes
 * TCPI itself, it only picks which of the two backend-computed fields to surface (domain-rules.md:
 * mirror the backend's own numbers, never a different formula on the frontend).
 */
export function resolveTcpi(
  evm: Pick<EvmResponseDto, 'tcpiBac' | 'tcpiEac'>,
  selectedVariantResult: EacVariantResponseDto | undefined,
): { value: string | null; basis: 'BAC' | 'EAC' } {
  const vac = selectedVariantResult?.vac ?? null
  const vacNumber = vac === null ? null : Number(vac)
  const forecastHasOverrun = vacNumber !== null && Number.isFinite(vacNumber) && vacNumber < 0

  if (forecastHasOverrun && evm.tcpiEac !== null) {
    return { value: evm.tcpiEac, basis: 'EAC' }
  }
  return { value: evm.tcpiBac, basis: 'BAC' }
}

export type MetricTone = 'neutral' | 'success' | 'danger' | 'gold'

function parseOrNull(value: string | null): number | null {
  if (value === null) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

/**
 * `value >= 0` -> favourable (success/green); `< 0` -> unfavourable (danger/red); `null` -> neutral.
 *
 * This is the *only* colour rule this module applies to an EAC-variant-derived number (used for
 * VAC), and it is deliberately derived from that one already-selected variant's own value, never
 * from a comparison across variants or from `variant` identity — see `EvmMetricsGrid.tsx`'s remarks
 * on why BAC/EAC/ETC stay neutral and VAC is the only dynamically-coloured one of the four tiles
 * S7-FE-02's DoD names. This is the actual fix for the ADR-0007 "ordering reverses when CPI > 1"
 * trap (domain-decisions.md §1.5): a rule of the shape "colour tile X red because variant Y is
 * usually the pessimistic one" is what the trap warns against; a rule of the shape "colour this
 * number red because it is negative" is safe under any ordering, because it never looks at which
 * variant produced the number.
 */
export function toneForSign(value: string | null): MetricTone {
  const parsed = parseOrNull(value)
  if (parsed === null) return 'neutral'
  return parsed >= 0 ? 'success' : 'danger'
}

/** `value >= threshold` -> favourable; `< threshold` -> unfavourable; `null` -> neutral. Used for
 * SPI/CPI (threshold 1) — these are never variant-dependent, so no ordering trap applies. */
export function toneForRatioThreshold(value: string | null, threshold = 1): MetricTone {
  const parsed = parseOrNull(value)
  if (parsed === null) return 'neutral'
  return parsed >= threshold ? 'success' : 'danger'
}
