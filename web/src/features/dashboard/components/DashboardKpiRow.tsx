import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { StatTile } from '../../../components'
import type { StatTileTone } from '../../../components'
import { formatMoneyMillions, formatPercent, formatRatio } from '../../../utils/format'
import {
  EAC_NULL_REASON_LABELS,
  EAC_VARIANT_FORMULA_LABELS,
  toneForRatioThreshold,
} from '../dashboardSelectors'
import type { MetricTone } from '../dashboardSelectors'
import type { DashboardResponseDto } from '../types'

export interface DashboardKpiRowProps {
  projectId: string
  dashboard: DashboardResponseDto
}

const TONE_TO_STAT_TILE_TONE: Record<MetricTone, StatTileTone> = {
  neutral: 'neutral',
  success: 'success',
  danger: 'danger',
}

function pct(value: string): string {
  return formatPercent(value)
}

function ratio(value: string | null): string | undefined {
  return value === null ? undefined : formatRatio(value)
}

/** The EAC tile's caption: the formula in MB units when computable (S8-FE-01 DoD's own literal
 * example — "BAC / CPI (MB)") plus the selected variant's VAC as a colored delta line (prototype's
 * "VAC +19 MB (ต่ำกว่างบ)" sub-line); the backend's own `eacNullReason` when not computable — never a
 * frontend-guessed explanation. */
function eacCaption(dashboard: DashboardResponseDto): ReactNode {
  if (!dashboard.eacComputable || dashboard.eac === null) {
    return dashboard.eacNullReason ? EAC_NULL_REASON_LABELS[dashboard.eacNullReason] : 'ยังคำนวณ EAC ไม่ได้'
  }

  const vacNumber = dashboard.vac === null ? null : Number(dashboard.vac)
  const hasVac = vacNumber !== null && Number.isFinite(vacNumber)

  return (
    <>
      {EAC_VARIANT_FORMULA_LABELS[dashboard.eacVariant]} (MB)
      {hasVac && dashboard.vac !== null && (
        <>
          <br />
          <span className={vacNumber! >= 0 ? 'text-success' : 'text-danger'}>
            VAC {vacNumber! >= 0 ? '+' : ''}
            {formatMoneyMillions(dashboard.vac)} ({vacNumber! >= 0 ? 'ต่ำกว่างบ' : 'เกินงบ'})
          </span>
        </>
      )}
    </>
  )
}

interface KpiTileViewModel {
  key: string
  label: string
  value: string | undefined
  tone: MetricTone
  caption: ReactNode
  to: string
}

/**
 * S8-FE-01's top KPI row (US-8.1) — 6 tiles, prototype position order preserved exactly (progress,
 * SPI, CPI, EAC, cumulative disbursement, cumulative rain days). Every tile is a real `Link` to the
 * screen it drills into, matching the prototype's `onClick` affordance on every tile.
 *
 * Only 4 of the 6 are backed by real data this sprint: **cumulative disbursement** (needs
 * `PaymentCertificate`/`ProjectFinanceLedger`, Sprint 9) and **cumulative rain days** (needs the
 * Weather Log module, unbuilt — no backend `Weather*` feature exists anywhere in this codebase) have
 * no data source at all. Rendered as explicit "not available yet" tiles (real "—", a reason caption,
 * still a working link to that screen's placeholder) rather than fabricated numbers — see this
 * sprint's frontend report.
 */
export function DashboardKpiRow({ projectId, dashboard }: DashboardKpiRowProps) {
  const tiles: KpiTileViewModel[] = [
    {
      key: 'progress',
      label: 'ความก้าวหน้า (Actual)',
      value: pct(dashboard.progressRollup.progressPercentage),
      tone: 'neutral',
      caption: 'Σ(Weight% × %ความคืบหน้า) ตาม WBS',
      to: `/app/${projectId}/wbs`,
    },
    {
      key: 'spi',
      label: 'SPI',
      value: ratio(dashboard.spi),
      tone: toneForRatioThreshold(dashboard.spi),
      caption: 'EV / PV — Schedule Performance',
      to: `/app/${projectId}/evm`,
    },
    {
      key: 'cpi',
      label: 'CPI',
      value: ratio(dashboard.cpi),
      tone: toneForRatioThreshold(dashboard.cpi),
      caption: 'EV / AC — Cost Performance',
      to: `/app/${projectId}/evm`,
    },
    {
      key: 'eac',
      label: 'EAC',
      value: dashboard.eacComputable && dashboard.eac !== null ? formatMoneyMillions(dashboard.eac) : undefined,
      tone: 'neutral',
      caption: eacCaption(dashboard),
      to: `/app/${projectId}/evm`,
    },
    {
      key: 'disbursement',
      label: 'เบิกจ่ายสะสม',
      value: undefined,
      tone: 'neutral',
      caption: 'ยังไม่พร้อมใช้งาน — รอ Payment Certificate (Sprint 9)',
      to: `/app/${projectId}/payment`,
    },
    {
      key: 'rain-days',
      label: 'วันฝนตกสะสม',
      value: undefined,
      tone: 'neutral',
      caption: 'ยังไม่พร้อมใช้งาน — Weather Log ยังไม่เปิดใช้งาน',
      to: `/app/${projectId}/weather`,
    },
  ]

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6" data-testid="dashboard-kpi-row">
      {tiles.map((tile) => (
        <Link key={tile.key} to={tile.to} className="block rounded-card focus:outline-none focus-visible:ring-2 focus-visible:ring-gold">
          <StatTile
            label={tile.label}
            value={tile.value}
            caption={tile.caption}
            tone={TONE_TO_STAT_TILE_TONE[tile.tone]}
            className="cursor-pointer transition-colors hover:border-gold"
          />
        </Link>
      ))}
    </div>
  )
}
