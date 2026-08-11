import { useMemo } from 'react'
import { StatTile } from '../../../components'
import { computeWeatherSummaryStats } from '../weatherStats'
import type { WeatherLogDto } from '../types'

export interface WeatherSummaryTilesProps {
  logs: WeatherLogDto[]
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
  /** Scrolls/filters the table to the unattributed rows — wired by `WeatherPage.tsx` so the tile is
   * an actionable prompt, not just a count (domain-rules.md §3.2/fixture W-09's own suggested UI
   * text: "ยื่นบันทึกแก้ไขเพื่อระบุ"). */
  onFocusUnattributed?: () => void
}

/**
 * The weather screen's three real, data-backed summary tiles (`weatherStats.ts`, computed over the
 * effective set only — `weatherChain.ts`). The prototype's own fourth tile ("พยากรณ์พรุ่งนี้",
 * `docs/ECM Planning Prototype.dc.html` ~line 427) is a weather-*forecast* figure with no backing
 * integration anywhere in this codebase (no forecast provider, no endpoint) — inventing a percentage
 * here would be exactly the kind of fabricated result CLAUDE.md forbids, so it is not reproduced.
 * The prototype's gold "สิทธิ์ขยายสัญญา (EOT)" tile is not reproduced here either — ADR-0020 requires
 * it relabelled and given room for the confidence/disclosure/driver detail a bare tile cannot hold,
 * so it is `EotEvaluationPanel.tsx`, its own card, immediately below this row.
 */
export function WeatherSummaryTiles({ logs, state, errorMessage, onFocusUnattributed }: WeatherSummaryTilesProps) {
  const stats = useMemo(() => computeWeatherSummaryStats(logs), [logs])
  const tileState = state === 'loading' ? 'loading' : state === 'error' ? 'error' : 'ready'

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <StatTile
        label="วันที่มีฝนตกบันทึกไว้"
        state={tileState}
        errorMessage={errorMessage}
        value={`${stats.rainDayCount.toLocaleString('th-TH')} วัน`}
        caption="นับจากบันทึกที่มีผลในปัจจุบันเท่านั้น (ไม่รวมรายการที่ถูกแก้ไข/เพิกถอนแล้ว)"
      />
      <StatTile
        label="วันหยุดงานจากสภาพอากาศ"
        state={tileState}
        errorMessage={errorMessage}
        tone="danger"
        value={`${stats.stoppageDayCount.toLocaleString('th-TH')} วัน`}
        caption="วันที่ Impact ≠ ไม่กระทบงาน — ยังไม่ผ่านเงื่อนไขการนับ EOT ทั้งหมด ดูผลจริงที่การประเมิน EOT ด้านล่าง"
      />
      <StatTile
        label="บันทึกที่ยังไม่ระบุกิจกรรม"
        state={tileState}
        errorMessage={errorMessage}
        tone={stats.unattributedCount > 0 ? 'warning' : 'neutral'}
        value={`${stats.unattributedCount.toLocaleString('th-TH')} รายการ`}
        caption={
          stats.unattributedCount > 0 && onFocusUnattributed ? (
            <button type="button" onClick={onFocusUnattributed} className="underline decoration-dotted hover:text-navy">
              ยื่นบันทึกแก้ไขเพื่อระบุกิจกรรม →
            </button>
          ) : (
            'บันทึกที่ระบุผลกระทบแต่ไม่ได้ระบุกิจกรรม จะไม่ถูกนับในการประเมิน EOT'
          )
        }
      />
    </div>
  )
}
