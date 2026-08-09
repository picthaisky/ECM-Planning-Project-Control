import { useMemo } from 'react'
import { formatMoneyMillions } from '../../../utils/format'
import { computeBarValueDomain, computeGroupLayout, scaleBarValue, zeroBaselineY } from '../cashFlowBarScale'
import type { CashFlowPeriodPointDto } from '../types'

export interface CashFlowBarChartProps {
  /** Ascending by `periodEnd`. The **last** entry may be the trailing "live"/still-open bucket
   * (`isClosed: false`) — every earlier entry is a frozen `EvmPeriodSnapshot` bucket (ADR-0009). A
   * single-entry array (task note #3: "until someone closes a period the chart shows essentially one
   * bar") is a normal, expected case, not degraded handling. */
  periods: CashFlowPeriodPointDto[]
  height?: number
}

const PAD_LEFT = 60
const PAD_RIGHT = 16
const PAD_TOP = 16
const PAD_BOTTOM = 34
const MIN_GROUP_WIDTH = 76
const MIN_CHART_WIDTH = 640

const PERIOD_LABEL_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  month: 'short',
  year: '2-digit',
  timeZone: 'Asia/Bangkok',
})

function toNumber(value: string): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : 0
}

function formatPeriodLabel(period: CashFlowPeriodPointDto): string {
  const end = new Date(period.periodEnd)
  const label = Number.isNaN(end.getTime()) ? '—' : PERIOD_LABEL_FORMATTER.format(end)
  return period.isClosed ? label : `${label} (live)`
}

/**
 * S8-FE-02's "กระแสเงินสดรายงวด (Cash Flow)" bar chart (prototype screen #6). Hand-rolled SVG, same
 * precedent as `features/evm/components/SCurveChart.tsx` (Sprint 7 established no chart library in
 * `package.json`; this follows that, not a new dependency).
 *
 * Plots **period** PV/EV/AC (not cumulative — the cumulative figures are `CashFlowSummaryTiles`'s
 * job) as 3 grouped bars per period, matching `SCurveChart`'s own PV(grey)/EV(gold)/AC(danger) color
 * convention exactly. The trailing live/still-open bucket (`isClosed: false`) is rendered at reduced
 * opacity with a "(live)" x-axis label suffix, so a sparse chart (often just one bar — task note #3,
 * "that's the intended design, not a bug") still reads as a deliberate, well-formed single cluster
 * rather than looking broken. Horizontally scrollable (not virtualized — ADR-0004's 10,000+-row
 * target is Gantt/WBS-scale, not this screen's dozens-of-periods scale at most) once there are more
 * periods than comfortably fit at a readable per-bar width.
 *
 * Receipts are never plotted here — ADR-0013 §5: they are a separate ledger, and today are typed as
 * wholly unavailable (`CashFlowReceiptsDto.isAvailable === false`), so there is no series to plot
 * even if this chart wanted to (see `CashFlowPage.tsx`'s remarks on keeping receipts/AC visually
 * separate everywhere on this screen).
 */
export function CashFlowBarChart({ periods, height = 240 }: CashFlowBarChartProps) {
  const width = Math.max(MIN_CHART_WIDTH, periods.length * MIN_GROUP_WIDTH + PAD_LEFT + PAD_RIGHT)
  const innerWidth = width - PAD_LEFT - PAD_RIGHT
  const innerHeight = height - PAD_TOP - PAD_BOTTOM

  const domain = useMemo(
    () =>
      computeBarValueDomain(
        periods.flatMap((p) => [toNumber(p.pvPeriod), toNumber(p.evPeriod), toNumber(p.acPeriod)]),
      ),
    [periods],
  )

  const zeroYLocal = zeroBaselineY(domain, innerHeight)
  const zeroY = PAD_TOP + zeroYLocal

  if (periods.length === 0) {
    return (
      <div className="flex h-56 items-center justify-center text-xs text-text-faint">
        ยังไม่มีข้อมูลงวดสำหรับวาดกราฟ Cash Flow
      </div>
    )
  }

  const ariaLabel = `กราฟกระแสเงินสดรายงวด PV/EV/AC จำนวน ${periods.length} งวด`

  return (
    <div>
      <div className="overflow-x-auto">
        <svg viewBox={`0 0 ${width} ${height}`} width={width} role="img" aria-label={ariaLabel} className="block">
          <line x1={PAD_LEFT} y1={PAD_TOP} x2={PAD_LEFT} y2={height - PAD_BOTTOM} strokeWidth={1} className="stroke-border" />
          <line x1={PAD_LEFT} y1={zeroY} x2={width - PAD_RIGHT} y2={zeroY} strokeWidth={1} className="stroke-border" />
          <text x={PAD_LEFT - 8} y={zeroY + 3} textAnchor="end" className="fill-text-faint text-[9.5px]">
            0
          </text>

          {periods.map((period, index) => {
            const layout = computeGroupLayout(index, periods.length, innerWidth)
            const bars = [
              { key: 'pv', value: toNumber(period.pvPeriod), x: layout.groupX, toneClass: 'fill-text-faint', label: 'PV' },
              { key: 'ev', value: toNumber(period.evPeriod), x: layout.groupX + layout.barWidth, toneClass: 'fill-gold', label: 'EV' },
              { key: 'ac', value: toNumber(period.acPeriod), x: layout.groupX + layout.barWidth * 2, toneClass: 'fill-danger', label: 'AC' },
            ]

            return (
              <g key={period.periodEnd} opacity={period.isClosed ? 1 : 0.6}>
                {bars.map((bar) => {
                  const valueY = scaleBarValue(bar.value, domain, innerHeight)
                  const barY = PAD_TOP + Math.min(valueY, zeroYLocal)
                  const barHeight = Math.max(Math.abs(zeroYLocal - valueY), 0.5)

                  return (
                    <rect
                      key={bar.key}
                      x={PAD_LEFT + bar.x}
                      y={barY}
                      width={Math.max(layout.barWidth - 2, 1)}
                      height={barHeight}
                      rx={1.5}
                      className={bar.toneClass}
                    >
                      <title>{`${bar.label} · ${formatPeriodLabel(period)}: ${formatMoneyMillions(bar.value)}`}</title>
                    </rect>
                  )
                })}
                <text
                  x={PAD_LEFT + layout.groupX + layout.groupWidth / 2}
                  y={height - PAD_BOTTOM + 14}
                  textAnchor="middle"
                  className="fill-text-faint text-[9px]"
                >
                  {formatPeriodLabel(period)}
                </text>
              </g>
            )
          })}
        </svg>
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-3.5 text-[10.5px] text-text-faint">
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-text-faint" />
          PV แผน
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-gold" />
          EV ผลงาน
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-danger" />
          AC ต้นทุน
        </span>
        <span className="ml-auto text-[10px]">แท่งโปร่งแสง = งวดที่ยังไม่ปิด (live)</span>
      </div>
    </div>
  )
}
