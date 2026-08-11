import { StatTile } from '../../../components'
import { formatMoney } from '../../../utils/format'
import type { BaselineComparisonDto } from '../types'

export interface BaselineSummaryTilesProps {
  comparison: BaselineComparisonDto | null
  state: 'ready' | 'loading' | 'error'
  errorMessage?: string
}

function signedDays(days: number | null): string | undefined {
  if (days === null) return undefined
  const sign = days > 0 ? '+' : ''
  return `${sign}${days.toLocaleString('th-TH')} วัน`
}

/**
 * S14-FE-01: the prototype's "เปรียบเทียบแผนปัจจุบัน vs {{ activeBlName }}" summary tiles
 * (`docs/ECM Planning Prototype.dc.html`'s Baseline screen). **Three tiles, not the prototype's
 * four.**
 *
 * The 4th tile, "Critical Path เปลี่ยนเส้นทาง" (did the critical path change), is deliberately
 * **not reproduced** — `BaselineComparisonDto`'s own backend doc comment states why:
 * `BaselineActivitySnapshot`'s field list (docs/10 §9/§3) never captured `IsCritical`/float at
 * capture time, so "did the critical path change" is not reconstructable from what S14-DB-01/BE-01
 * actually persist. Fabricating a plausible-looking answer here would be exactly the kind of
 * "silently guessed number" this whole codebase's culture refuses (ADR-0012/ADR-0017's same fail-
 * closed discipline). `BaselineComparisonTable`'s own per-row "Critical" badge still shows the
 * *current* criticality (`isCritical`, which the delta DTO does carry) — that is a different,
 * honest, row-level fact, not the aggregate "changed since baseline" claim this tile would need.
 */
export function BaselineSummaryTiles({ comparison, state, errorMessage }: BaselineSummaryTilesProps) {
  const bacVarianceNumber = comparison ? Number(comparison.bacVarianceAmount) : null
  const bacTone = bacVarianceNumber === null || Number.isNaN(bacVarianceNumber) ? 'neutral' : bacVarianceNumber > 0 ? 'danger' : 'success'
  const finishTone =
    comparison?.projectFinishVarianceDays === null || comparison?.projectFinishVarianceDays === undefined
      ? 'neutral'
      : comparison.projectFinishVarianceDays > 0
        ? 'danger'
        : 'success'
  const driftTone = comparison && comparison.driftedActivityCount > 0 ? 'danger' : 'success'

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3" data-testid="baseline-summary-tiles">
      <StatTile
        label="วันแล้วเสร็จโครงการ เปลี่ยนแปลง"
        value={comparison ? (signedDays(comparison.projectFinishVarianceDays) ?? '—') : undefined}
        caption={comparison?.baselineName ? `เทียบกับ ${comparison.baselineName}` : undefined}
        tone={finishTone}
        state={state}
        errorMessage={errorMessage}
      />
      <StatTile
        label="กิจกรรมที่เลื่อนช้ากว่าแผน"
        value={comparison ? `${comparison.driftedActivityCount.toLocaleString('th-TH')} / ${comparison.totalActivityCount.toLocaleString('th-TH')}` : undefined}
        caption="จำนวนกิจกรรม (วันสิ้นสุดปัจจุบันช้ากว่า Baseline)"
        tone={driftTone}
        state={state}
        errorMessage={errorMessage}
      />
      <StatTile
        label="BAC เปลี่ยนแปลง"
        value={comparison ? `${bacVarianceNumber !== null && bacVarianceNumber > 0 ? '+' : ''}${formatMoney(comparison.bacVarianceAmount)} บาท` : undefined}
        caption="ปัจจุบัน − Baseline (รวมผลจาก VO ที่อนุมัติแล้ว)"
        tone={bacTone}
        state={state}
        errorMessage={errorMessage}
      />
    </div>
  )
}
