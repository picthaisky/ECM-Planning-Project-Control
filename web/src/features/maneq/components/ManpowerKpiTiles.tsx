import { StatTile } from '../../../components'
import type { StatTileTone } from '../../../components'
import { formatHours, formatWorkerCount, PI_NULL_REASON_LABELS } from '../maneqLabels'
import { manningVarianceBand, parseManpowerDecimal, piBand } from '../maneqStats'
import type { SectionState } from '../useManpowerOverview'
import type { ProductivityIndexResponseDto } from '../types'
import { formatRatio } from '../../../utils/format'

export interface ManpowerKpiTilesProps {
  today: SectionState<ProductivityIndexResponseDto>
  monthToDate: SectionState<ProductivityIndexResponseDto>
  cumulativePi: SectionState<ProductivityIndexResponseDto>
}

const MANNING_BAND_TONE: Record<ReturnType<typeof manningVarianceBand>, StatTileTone> = {
  // §9.2/§5.3's neutral-palette ruling — over-manning is never "success" (green). "on plan"/"no
  // plan" use the design system's neutral navy tone; "above" uses gold (never green — the fixed
  // prototype defect at `ECM Planning Prototype.dc.html:872` this feature exists to correct, see
  // `maneqStats.ts#manningVarianceBand`'s own remarks); "below" is a genuine shortfall (danger red).
  onplan: 'neutral',
  below: 'danger',
  above: 'gold',
  noplan: 'neutral',
}

const PI_BAND_TONE: Record<ReturnType<typeof piBand>, StatTileTone> = {
  success: 'success',
  gold: 'warning',
  danger: 'danger',
  null: 'neutral',
}

/**
 * S12-FE-02's four KPI tiles (prototype ~line 537, corrected per domain-rules.md §9.3):
 *
 * 1. **กำลังคนวันนี้** — real `actualWorkerCount` for today + delta vs `plannedWorkerCount`, using the
 *    **neutral** variance palette (never the prototype's `d >= -10 ? green : red`, which paints
 *    unlimited over-manning green — the defect `domain-expert` flagged at
 *    `ECM Planning Prototype.dc.html:872`).
 * 2. **เครื่องจักรทำงาน** — renders "—" honestly: the Sprint 12 backend records equipment hours per row
 *    but exposes **no** read endpoint for an aggregate utilisation/availability figure (confirmed
 *    directly against `ProjectManpowerLogsController.cs` — only `productivity-index` is a `GET`).
 *    This is a stated backend gap, not a frontend shortcut — a fabricated "14 / 16" here would be
 *    exactly the kind of invented result CLAUDE.md forbids.
 * 3. **ชม.ทำงานสะสม (เดือนนี้)** — real `actualManHoursTotal` for the month-to-date bucket.
 * 4. **Productivity Index** — real $PI^{cum}(t)$. **This is the tile the whole feature's D8 gate
 *    exists for.** `null` renders "—" with `productivityIndexNullReason`'s Thai copy as the caption
 *    (never a bare dash — this task's own instruction), and a coverage badge appears whenever
 *    coverage is below 100%.
 */
export function ManpowerKpiTiles({ today, monthToDate, cumulativePi }: ManpowerKpiTilesProps) {
  const actualWorkerCount = today.data?.actualWorkerCount ?? null
  const plannedWorkerCount = today.data?.plannedWorkerCount ?? null
  const manningDelta = actualWorkerCount !== null && plannedWorkerCount !== null ? actualWorkerCount - plannedWorkerCount : null
  const manningBand = actualWorkerCount !== null ? manningVarianceBand(actualWorkerCount, plannedWorkerCount) : 'noplan'

  const monthToDateHours = parseManpowerDecimal(monthToDate.data?.actualManHoursTotal ?? null)

  const piValue = parseManpowerDecimal(cumulativePi.data?.productivityIndex ?? null)
  const piReason = cumulativePi.data?.productivityIndexNullReason ?? null
  const coverage = parseManpowerDecimal(cumulativePi.data?.coveragePercentage ?? null)
  const band = piBand(piValue)

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <StatTile
        label="กำลังคนวันนี้"
        state={today.state}
        errorMessage={today.error ?? undefined}
        tone={MANNING_BAND_TONE[manningBand]}
        value={actualWorkerCount !== null ? `${formatWorkerCount(actualWorkerCount)} คน` : undefined}
        caption={
          plannedWorkerCount === null
            ? 'ยังไม่มีแผนกำลังคนสำหรับวันนี้ — ไม่แสดงส่วนต่าง'
            : `แผน ${formatWorkerCount(plannedWorkerCount)} คน (${manningDelta !== null && manningDelta >= 0 ? '+' : ''}${manningDelta ?? '—'})`
        }
      />

      <StatTile
        label="เครื่องจักรทำงาน"
        state="ready"
        tone="neutral"
        value="—"
        caption="ยังไม่มี endpoint สำหรับอ่านสรุปการใช้งานเครื่องจักร (บันทึกได้ แต่ยังไม่มีอ่านสรุป)"
      />

      <StatTile
        label="ชม.ทำงานสะสม (เดือนนี้)"
        state={monthToDate.state}
        errorMessage={monthToDate.error ?? undefined}
        value={monthToDateHours !== null ? `${formatHours(monthToDateHours)} ชม.` : undefined}
      />

      <StatTile
        label="Productivity Index"
        state={cumulativePi.state}
        errorMessage={cumulativePi.error ?? undefined}
        tone={PI_BAND_TONE[band]}
        value={piValue !== null ? formatRatio(piValue) : '—'}
        caption={
          piValue === null
            ? (piReason ? PI_NULL_REASON_LABELS[piReason] : 'ไม่มีข้อมูล')
            : coverage !== null && coverage < 100
              ? `EV ต่อ ชม.แรงงาน / แผน — ครอบคลุมข้อมูล ${formatRatio(coverage)}%`
              : 'EV ต่อ ชม.แรงงาน / แผน'
        }
      />
    </div>
  )
}
