import { useMemo } from 'react'
import { formatMoney } from '../../../utils/format'
import { EAC_NULL_REASON_LABELS } from '../evmSelectors'
import {
  buildPolylinePath,
  computeTimeDomain,
  computeValueDomain,
  scaleTime,
  scaleValue,
} from '../sCurveScale'
import type { EacNullReason, SCurvePoint } from '../types'

export type { SCurvePoint }

export interface SCurveChartProps {
  /** Ascending by date; the **last** entry is the live/current point (today's data date) — every
   * earlier entry must come from a closed `EvmPeriodSnapshot`, never a live recomputation for a
   * past date (ADR-0009; see `EvmPage.tsx`'s remarks on how this array is assembled). */
  points: SCurvePoint[]
  bac: string
  /** The *currently selected* EAC variant's own forecast value, or `null` when it is not
   * computable — no forecast line is drawn when `null` (the honest-null rule extends to the chart,
   * not just the metric tiles). */
  forecastEac: string | null
  /** Set alongside `forecastEac === null` so the chart can say *why* there is no forecast line,
   * using the backend's own reason code (never re-derived). */
  forecastUnavailableReason?: EacNullReason | null
  width?: number
  height?: number
}

const CHART_WIDTH = 720
const CHART_HEIGHT = 280
const PAD_LEFT = 68
const PAD_RIGHT = 16
const PAD_TOP = 20
const PAD_BOTTOM = 16

const dataDateCaptionFormatter = new Intl.DateTimeFormat('th-TH', { dateStyle: 'long', timeZone: 'Asia/Bangkok' })

function toNumber(value: string): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : 0
}

/**
 * S7-FE-04: the EVM S-Curve. Solid lines for EV (gold) and AC (danger/red); a dashed line for PV
 * (text-faint grey) since — unlike the working prototype's decorative mock, which draws a dashed PV
 * line stretching across the *entire* chart including future dates — this real implementation can
 * only plot PV for dates a real API response actually returned (`points`, bounded by the live data
 * date): no endpoint in this sprint's contract exposes a forward-looking planned-value curve, and
 * inventing one would fabricate data this whole feature is built to avoid doing. A dashed line for
 * the EAC forecast (secondary/slate-blue), continuing from the live **AC** point (not EV — EAC is
 * literally `AC + ETC`, a projection of the *cost* curve, per evm-formulas.md) to a stylized future
 * x position (`FORECAST_TIME_EXTENSION_RATIO`, `sCurveScale.ts`) at the selected variant's own EAC
 * value — the vertical position is a real, API-computed number; only the horizontal position is
 * illustrative, since no endpoint this sprint exposes a forecast *completion date*.
 *
 * Deliberately renders straight (`M`/`L`) segments between real data points, never a Bezier smooth
 * — see `sCurveScale.ts#buildPolylinePath`'s remarks.
 *
 * Colours are Tailwind utility classes generated from `src/config/theme.ts` only
 * (`local/no-raw-hex-colors` lint rule) — never an inline hex value.
 */
export function SCurveChart({
  points,
  bac,
  forecastEac,
  forecastUnavailableReason = null,
  width = CHART_WIDTH,
  height = CHART_HEIGHT,
}: SCurveChartProps) {
  const innerWidth = width - PAD_LEFT - PAD_RIGHT
  const innerHeight = height - PAD_TOP - PAD_BOTTOM

  const series = useMemo(
    () =>
      points.map((p) => ({
        time: new Date(p.dataDate).getTime(),
        pv: toNumber(p.pv),
        ev: toNumber(p.ev),
        ac: toNumber(p.ac),
      })),
    [points],
  )

  const bacValue = toNumber(bac)
  const forecastValue = forecastEac === null ? null : toNumber(forecastEac)

  const timeDomain = useMemo(() => computeTimeDomain(series.map((s) => s.time)), [series])
  const valueDomain = useMemo(
    () => computeValueDomain(series, bacValue, forecastValue),
    [series, bacValue, forecastValue],
  )

  const project = (time: number, value: number) => ({
    x: PAD_LEFT + scaleTime(time, timeDomain, innerWidth),
    y: PAD_TOP + scaleValue(value, valueDomain, innerHeight),
  })

  const pvPath = buildPolylinePath(series.map((s) => project(s.time, s.pv)))
  const evPath = buildPolylinePath(series.map((s) => project(s.time, s.ev)))
  const acPath = buildPolylinePath(series.map((s) => project(s.time, s.ac)))

  const last = series.length > 0 ? series[series.length - 1] : null
  const forecastPath =
    last && forecastValue !== null
      ? buildPolylinePath([project(last.time, last.ac), project(timeDomain.forecastTime, forecastValue)])
      : null

  const dataDateX = last ? PAD_LEFT + scaleTime(last.time, timeDomain, innerWidth) : null
  const bacY = PAD_TOP + scaleValue(bacValue, valueDomain, innerHeight)
  const zeroY = PAD_TOP + scaleValue(valueDomain.minValue, valueDomain, innerHeight)
  const evPoint = last ? project(last.time, last.ev) : null
  const acPoint = last ? project(last.time, last.ac) : null

  const ariaLabel = last
    ? `กราฟ S-Curve: PV, EV และ AC ถึงวันที่ข้อมูล ${dataDateCaptionFormatter.format(new Date(last.time))}` +
      (forecastValue !== null ? ` พร้อมเส้นพยากรณ์ EAC ${formatMoney(forecastValue)} บาท` : '')
    : 'กราฟ S-Curve: ยังไม่มีข้อมูล'

  if (series.length === 0) {
    return (
      <div className="flex h-56 items-center justify-center text-xs text-text-faint">
        ยังไม่มีข้อมูลสำหรับวาดกราฟ S-Curve
      </div>
    )
  }

  return (
    <div>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={ariaLabel} className="block w-full">
        {/* Axes */}
        <line x1={PAD_LEFT} y1={PAD_TOP} x2={PAD_LEFT} y2={height - PAD_BOTTOM} strokeWidth={1} className="stroke-border" />
        <line x1={PAD_LEFT} y1={zeroY} x2={width - PAD_RIGHT} y2={zeroY} strokeWidth={1} className="stroke-border" />
        <text x={PAD_LEFT - 8} y={zeroY + 3} textAnchor="end" className="fill-text-faint text-[9.5px]">
          0
        </text>

        {/* BAC reference gridline */}
        <line
          x1={PAD_LEFT}
          y1={bacY}
          x2={width - PAD_RIGHT}
          y2={bacY}
          strokeWidth={1}
          strokeDasharray="3 3"
          className="stroke-border-subtle"
        />
        <text x={PAD_LEFT - 8} y={bacY + 3} textAnchor="end" className="fill-text-faint text-[9.5px]">
          BAC {formatMoney(bac)}
        </text>

        {/* PV (dashed — plotted only over real, returned data points; see this component's remarks) */}
        <path d={pvPath} fill="none" strokeWidth={2} strokeDasharray="6 5" className="stroke-text-faint" />
        {/* AC (solid) */}
        <path d={acPath} fill="none" strokeWidth={2} className="stroke-danger" />
        {/* EV (solid, drawn last so it sits on top like the prototype's layering) */}
        <path d={evPath} fill="none" strokeWidth={3} className="stroke-gold" />

        {forecastPath && (
          <path d={forecastPath} fill="none" strokeWidth={2} strokeDasharray="2 4" className="stroke-secondary" />
        )}

        {dataDateX !== null && (
          <>
            <line
              x1={dataDateX}
              y1={PAD_TOP - 6}
              x2={dataDateX}
              y2={height - PAD_BOTTOM}
              strokeWidth={1}
              strokeDasharray="3 3"
              className="stroke-navy"
            />
            <text x={dataDateX + 4} y={PAD_TOP + 2} className="fill-navy text-[9.5px] font-semibold">
              Data Date · {dataDateCaptionFormatter.format(new Date(last!.time))}
            </text>
          </>
        )}

        {evPoint && <circle cx={evPoint.x} cy={evPoint.y} r={4} className="fill-gold" />}
        {acPoint && <circle cx={acPoint.x} cy={acPoint.y} r={3} className="fill-danger" />}
      </svg>

      <p className="mt-2 text-[10.5px] leading-snug text-text-faint">
        จุด PV/EV/AC ก่อนวัน Data Date มาจากงวดที่ปิดแล้ว (EvmPeriodSnapshot) ไม่ใช่การคำนวณสด
        (ADR-0009) · เส้นประสีเทา = PV · เส้นประสีกรมท่า = Data Date · เส้นประสีน้ำเงินเทา =
        ทิศทางพยากรณ์ EAC (ตำแหน่งแนวนอนเป็นภาพประกอบ ไม่ใช่วันที่แล้วเสร็จจริง)
        {forecastValue === null && forecastUnavailableReason && (
          <>
            {' · '}
            <span className="text-text-muted">
              ยังไม่แสดงเส้นพยากรณ์ EAC: {EAC_NULL_REASON_LABELS[forecastUnavailableReason]}
            </span>
          </>
        )}
      </p>
    </div>
  )
}
