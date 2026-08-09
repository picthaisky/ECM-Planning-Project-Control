import { formatPercent } from '../../utils/format'
import type { GanttActivityDto } from '../gantt'
import type { WbsTreeNodeDto } from '../wbs'
import type { DashboardWeightWarningDto, EacNullReason, EacVariant } from './types'

/**
 * Thai translation of every `EacNullReason` the backend can return — duplicated verbatim from
 * `features/evm/evmSelectors.ts#EAC_NULL_REASON_LABELS` (see `types.ts`'s remarks on why this
 * feature owns its own copy rather than importing the value). Keep both copies in sync if the
 * backend enum ever changes — flagged to `knowledge-curator` as a promotion candidate now that it is
 * used in 2 features.
 */
export const EAC_NULL_REASON_LABELS: Record<EacNullReason, string> = {
  NotStarted: 'โครงการยังไม่เริ่มดำเนินการ — PV, EV และ AC ยังเป็น 0 ทั้งหมด',
  NoActualCost: 'ยังไม่มีข้อมูลค่าใช้จ่ายจริง (Actual Cost) ของโครงการนี้',
  NoPlannedValue: 'โครงการนี้ยังไม่มีเส้นฐาน (Baseline) จึงไม่มีมูลค่าตามแผน (PV)',
  ZeroCpi: 'มีค่าใช้จ่ายจริงเกิดขึ้นแล้วแต่ยังไม่มีผลงานที่ทำได้ (EV) จึงคำนวณ CPI ไม่ได้',
  ManualEtcNotSet: 'ยังไม่ได้กรอกประมาณการงานที่เหลือ (Bottom-Up ETC) — พร้อมใช้งานใน Sprint 14',
  CustomPfNotSet: 'ยังไม่ได้กำหนดตัวคูณผลการดำเนินงานเอง (Custom Performance Factor) — พร้อมใช้งานใน Sprint 14',
}

/** Formula caption per variant, shown under the Dashboard's EAC tile (S8-FE-01 DoD: "ทุก tile มี
 * บรรทัดสูตรกำกับ (เช่น 'BAC / CPI (MB)')"). Duplicated from `features/evm/evmSelectors.ts#
 * VARIANT_FORMULA_LABELS` — same content, same promotion note as `EAC_NULL_REASON_LABELS` above. */
export const EAC_VARIANT_FORMULA_LABELS: Record<EacVariant, string> = {
  CpiBased: 'BAC / CPI',
  Atypical: 'AC + (BAC − EV)',
  CpiSpiBased: 'AC + (BAC − EV) / (CPI × SPI)',
  BottomUpEtc: 'AC + ETC (ประมาณการเอง)',
  CustomPf: 'AC + PF × (BAC − EV)',
}

/** Stable warning codes this screen's `warnings[]` can carry (`EvmWarningCodes`, reused verbatim by
 * `DashboardResponseDto.Warnings` — see that DTO's own remarks: the Dashboard never adds a warning
 * code of its own, it only forwards the EVM engine's). Unmapped codes still render (never dropped) —
 * see `describeWarning`. */
const DASHBOARD_WARNING_LABELS: Record<string, string> = {
  EarnedValueExceedsBudget:
    'มูลค่างานที่ทำได้ (EV) เกินงบประมาณ (BAC) — ตรวจสอบความคืบหน้าหรือ Weight ที่บันทึกไว้อีกครั้ง',
  ActualCostIsNegative:
    'ยอดต้นทุนจริงสะสม (AC) ติดลบ (มีรายการปรับปรุง/กลับรายการมากกว่าที่บันทึกไว้) — ตัวเลขที่อิงกับ CPI ในหน้านี้ควรใช้ด้วยความระมัดระวัง',
}

/** Never hides an unmapped code — worst case shows the raw backend code, same as
 * `features/evm/`'s current `evm.warnings.join(' · ')` behavior, strictly better when a mapping
 * exists. */
export function describeWarning(code: string): string {
  return DASHBOARD_WARNING_LABELS[code] ?? code
}

export type MetricTone = 'neutral' | 'success' | 'danger'

function parseOrNull(value: string | null): number | null {
  if (value === null) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

/** `value >= 0` -> favourable (success/green); `< 0` -> unfavourable (danger/red); `null` -> neutral.
 * Duplicated from `features/evm/evmSelectors.ts#toneForSign` (same rule, same reasoning: derived
 * only from the number's own sign, never from which EAC variant produced it — ADR-0007's "ordering
 * reverses when CPI > 1" trap). */
export function toneForSign(value: string | null): MetricTone {
  const parsed = parseOrNull(value)
  if (parsed === null) return 'neutral'
  return parsed >= 0 ? 'success' : 'danger'
}

/** `value >= threshold` -> favourable; `< threshold` -> unfavourable; `null` -> neutral. Used for
 * SPI/CPI (threshold 1). Duplicated from `features/evm/evmSelectors.ts#toneForRatioThreshold`. */
export function toneForRatioThreshold(value: string | null, threshold = 1): MetricTone {
  const parsed = parseOrNull(value)
  if (parsed === null) return 'neutral'
  return parsed >= threshold ? 'success' : 'danger'
}

/**
 * S8-FE-01's "Critical Path — งานวิกฤตใกล้ถึงกำหนด" preview: the top `limit` critical activities
 * soonest due, from the real `GET .../gantt` read (Sprint 6) — `DashboardResponseDto` itself carries
 * no schedule/critical-path data at all, and there is no dashboard-specific critical-path endpoint
 * (adding one is a backend change, out of scope for this frontend-only sprint slice), so this reuses
 * the already-real, already-computed `isCritical`/`totalFloat` the Sprint 5 CPM engine wrote.
 *
 * Pure/unit-testable on purpose — `components/DashboardCriticalPathPreview.tsx` only renders
 * whatever this returns, it never re-filters/re-sorts itself.
 */
export function selectTopCriticalActivities(
  activities: readonly GanttActivityDto[],
  limit = 4,
): GanttActivityDto[] {
  return activities
    .filter((activity) => activity.isCritical)
    .slice()
    .sort((a, b) => new Date(a.plannedFinish).getTime() - new Date(b.plannedFinish).getTime())
    .slice(0, limit)
}

export interface WbsNodeLabel {
  code: string
  title: string
}

/** Flattens the real `GET .../wbs-tree` read (Sprint 4) into an id -> {code, title} lookup, used to
 * turn `DashboardWeightWarningDto.wbsNodeId`/`DashboardProgressRollupDto.mixedScopeWbsNodeIds` (bare
 * `Guid`s on the wire) into a human-readable label — a real, non-fabricated cross-reference between
 * two already-fetched real datasets (never invents a name). */
export function buildWbsNodeLabelLookup(rootNodes: readonly WbsTreeNodeDto[]): Map<string, WbsNodeLabel> {
  const lookup = new Map<string, WbsNodeLabel>()
  const stack = [...rootNodes]
  while (stack.length > 0) {
    const node = stack.pop()!
    lookup.set(node.id, { code: node.code, title: node.title })
    stack.push(...node.children)
  }
  return lookup
}

const ROOT_LEVEL_LABEL = 'ระดับบนสุดของโครงการ (Root)'

function labelFor(wbsNodeId: string | null, lookup: ReadonlyMap<string, WbsNodeLabel>): string {
  if (wbsNodeId === null) return ROOT_LEVEL_LABEL
  const found = lookup.get(wbsNodeId)
  return found ? `${found.code} ${found.title}` : wbsNodeId
}

/** Thai copy for one `DashboardWeightWarningDto`, sourced from `WbsRollupWeightWarning`'s own real
 * meaning ("this level's children's WeightPercentage did not sum to 100.00") — never a generic
 * "something's wrong" message. */
export function describeWeightWarning(
  warning: DashboardWeightWarningDto,
  lookup: ReadonlyMap<string, WbsNodeLabel>,
): string {
  const label = labelFor(warning.wbsNodeId, lookup)
  return `${label}: น้ำหนักรวมของหมวดงานย่อย ${warning.childCount} รายการ = ${formatPercent(warning.weightSum)} (ไม่ครบ 100%)`
}

/** Thai copy for one mixed-scope node id, sourced from `WbsProgressRollupCalculator`'s own real
 * meaning ("this node has both child nodes and its own direct activities — only the child-subtree
 * rollup counts, the direct activities are excluded from this node's own figure"). */
export function describeMixedScopeNode(nodeId: string, lookup: ReadonlyMap<string, WbsNodeLabel>): string {
  const label = labelFor(nodeId, lookup)
  return `${label}: มีทั้งกิจกรรมย่อยโดยตรงและหมวดงานย่อยในระดับเดียวกัน ระบบใช้ความคืบหน้าจากหมวดงานย่อยเท่านั้น (ไม่รวมกิจกรรมที่อยู่ใต้หมวดงานนี้โดยตรง)`
}
