import { useMemo } from 'react'
import { computeManpowerBarDomain, computeManpowerBarSlot, scaleManpowerBarValue } from '../maneqBarScale'
import { formatHours, formatShortDay } from '../maneqLabels'
import { manningVarianceBand, parseManpowerDecimal, piBand } from '../maneqStats'
import { formatRatio } from '../../../utils/format'
import type { HistogramPoint } from '../useManpowerOverview'

export interface ManpowerHistogramChartProps {
  points: HistogramPoint[]
  height?: number
}

const PAD_LEFT = 44
const PAD_RIGHT = 12
const PAD_TOP = 12
const PAD_BOTTOM = 30

/** §9.2's bar-colour rule, applied to the man-hours bar itself. Every band here is real, computed
 * data (`actualWorkerCount`/`plannedWorkerCount` from a real exactly-one-day query — see
 * `useManpowerOverview.ts`'s remarks) — **not** the man-hours-vs-plan-hours variance §9.2 literally
 * describes, because no Sprint 12 endpoint exposes planned *hours* at all (`ManpowerPlan`/derived
 * plan-hours have no `GET` anywhere — confirmed against `ProjectManpowerLogsController.cs`). Using
 * the real headcount-vs-planned-headcount signal for the bar colour is the closest honest proxy
 * available; a manufactured plan-hours figure would not be.
 */
const BAND_BAR_CLASS: Record<ReturnType<typeof manningVarianceBand>, string> = {
  onplan: 'fill-secondary',
  below: 'fill-danger',
  above: 'fill-gold',
  noplan: 'fill-border',
}

const PI_DOT_CLASS: Record<ReturnType<typeof piBand>, string> = {
  success: 'fill-success',
  gold: 'fill-gold',
  danger: 'fill-danger',
  null: 'fill-border',
}

/**
 * S12-FE-02's histogram (prototype "Histogram กำลังคน 7 วันล่าสุด", ~line 565) + the PI-trend row
 * beneath it. Hand-rolled SVG, same precedent as `features/cash/components/CashFlowBarChart.tsx` (no
 * chart library in `package.json`). Y-axis is **man-hours**, not headcount (domain-rules.md §9.1:
 * "Hours are additive across shifts and overtime and are the quantity PI actually uses").
 *
 * PI points that are `null` are rendered as a hollow marker, never plotted as `0` and never silently
 * dropped (§9.2: "PI trend nulls are gaps... draw the point as a hollow marker... with the reason in
 * the tooltip").
 */
export function ManpowerHistogramChart({ points, height = 200 }: ManpowerHistogramChartProps) {
  const width = Math.max(360, points.length * 56 + PAD_LEFT + PAD_RIGHT)
  const innerWidth = width - PAD_LEFT - PAD_RIGHT
  const innerHeight = height - PAD_TOP - PAD_BOTTOM

  const hoursByDay = useMemo(
    () => points.map((point) => parseManpowerDecimal(point.response.actualManHoursTotal) ?? 0),
    [points],
  )
  const domain = useMemo(() => computeManpowerBarDomain(hoursByDay), [hoursByDay])

  if (points.length === 0) {
    return (
      <div className="flex h-40 items-center justify-center text-xs text-text-faint">
        ยังไม่มีข้อมูลสำหรับวาด Histogram
      </div>
    )
  }

  const ariaLabel = `กราฟ Histogram กำลังคน ${points.length} วันล่าสุด`

  return (
    <div>
      <div className="overflow-x-auto">
        <svg viewBox={`0 0 ${width} ${height}`} width={width} role="img" aria-label={ariaLabel} className="block">
          <line x1={PAD_LEFT} y1={PAD_TOP} x2={PAD_LEFT} y2={height - PAD_BOTTOM} strokeWidth={1} className="stroke-border" />
          <line
            x1={PAD_LEFT}
            y1={PAD_TOP + innerHeight}
            x2={width - PAD_RIGHT}
            y2={PAD_TOP + innerHeight}
            strokeWidth={1}
            className="stroke-border"
          />
          <text x={PAD_LEFT - 6} y={PAD_TOP + innerHeight + 3} textAnchor="end" className="fill-text-faint text-[9px]">
            0
          </text>

          {points.map((point, index) => {
            const hours = hoursByDay[index]
            const slot = computeManpowerBarSlot(index, points.length, innerWidth)
            const valueY = scaleManpowerBarValue(hours, domain, innerHeight)
            const barHeight = Math.max(innerHeight - valueY, 0.5)
            const band = manningVarianceBand(point.response.actualWorkerCount ?? 0, point.response.plannedWorkerCount ?? null)
            const piValue = parseManpowerDecimal(point.response.productivityIndex)
            const pBand = piBand(piValue)
            const dotX = PAD_LEFT + slot.x + slot.width / 2
            const dotY = height - 8

            return (
              <g key={point.dateInputValue}>
                <rect
                  x={PAD_LEFT + slot.x}
                  y={PAD_TOP + innerHeight - barHeight}
                  width={slot.width}
                  height={barHeight}
                  rx={1.5}
                  className={BAND_BAR_CLASS[band]}
                >
                  <title>{`${formatShortDay(point.dateInputValue)} · ${formatHours(hours)} ชม.`}</title>
                </rect>
                <text x={dotX} y={height - 18} textAnchor="middle" className="fill-text-faint text-[9px]">
                  {formatShortDay(point.dateInputValue)}
                </text>
                {piValue === null ? (
                  <circle cx={dotX} cy={dotY} r={3} className="fill-surface stroke-border" strokeWidth={1.5}>
                    <title>{`PI ${formatShortDay(point.dateInputValue)}: ไม่มีข้อมูล`}</title>
                  </circle>
                ) : (
                  <circle cx={dotX} cy={dotY} r={3} className={PI_DOT_CLASS[pBand]}>
                    <title>{`PI ${formatShortDay(point.dateInputValue)}: ${formatRatio(piValue)}`}</title>
                  </circle>
                )}
              </g>
            )
          })}
        </svg>
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-3.5 text-[10.5px] text-text-faint">
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-secondary" />
          กำลังคนตามแผน ±5%
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-danger" />
          ต่ำกว่าแผน
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-gold" />
          สูงกว่าแผน
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="h-2 w-2 rounded-full bg-success" />
          PI ≥ 0.95
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="h-2 w-2 rounded-full bg-danger" />
          PI &lt; 0.85
        </span>
        <span className="ml-auto text-[10px]">แท่ง = ชั่วโมงแรงงานรวม (ไม่มีข้อมูลชั่วโมงตามแผนจากระบบในขณะนี้) · จุด = Productivity Index รายวัน</span>
      </div>
    </div>
  )
}
