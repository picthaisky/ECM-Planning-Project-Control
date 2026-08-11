import { Link } from 'react-router-dom'
import { ChartCard } from '../../../components'
import { SCurveChart, buildSCurvePoints } from '../../evm'
import type { EvmSnapshotDto } from '../../evm'
import type { DashboardResponseDto } from '../types'

export interface DashboardSCurvePreviewProps {
  projectId: string
  dashboard: DashboardResponseDto
  snapshots: EvmSnapshotDto[]
  snapshotsLoadState: 'loading' | 'ready' | 'error'
  snapshotsLoadError: string | null
}

/**
 * S8-FE-01's "EVM S-Curve" preview card (prototype row 2, left column). Reuses the real
 * `features/evm` S-Curve machinery wholesale (`SCurveChart` + `buildSCurvePoints`) rather than
 * forking a second hand-rolled SVG renderer — same chart, smaller (`height=200` vs the EVM page's
 * `280`), fed by `DashboardResponseDto`'s own live Bac/Pv/Ev/Ac point plus the same closed-period
 * `EvmPeriodSnapshot` history the EVM page reads (ADR-0009: historical points are frozen, never a
 * live recomputation for a past date) — a second `GET .../evm/snapshots` call, independent of the
 * primary `GET .../dashboard` load so one failing does not blank the other (mirrors
 * `features/evm/EvmPage.tsx`'s own `useEvmData`/`useEvmSnapshots` split).
 *
 * This is a real, live-data preview, not a decorative mock — the DoD's "S-Curve preview ... ตรง
 * prototype" is met on data honesty, not merely on layout position.
 */
export function DashboardSCurvePreview({
  projectId,
  dashboard,
  snapshots,
  snapshotsLoadState,
  snapshotsLoadError,
}: DashboardSCurvePreviewProps) {
  const points = buildSCurvePoints(
    { dataDate: dashboard.dataDate, pv: dashboard.pv, ev: dashboard.ev, ac: dashboard.ac },
    snapshots,
  )

  return (
    <ChartCard
      title="EVM S-Curve"
      subtitle="PV แผน · EV ผลงาน · AC ต้นทุน"
      state={snapshotsLoadState === 'loading' ? 'loading' : 'ready'}
    >
      <SCurveChart
        points={points}
        bac={dashboard.bac}
        forecastEac={dashboard.eacComputable ? dashboard.eac : null}
        forecastUnavailableReason={dashboard.eacNullReason}
        height={200}
      />
      {snapshotsLoadState === 'error' && (
        <p role="alert" className="mt-2 text-[10.5px] text-danger">
          {snapshotsLoadError ?? 'โหลดประวัติงวด EVM ไม่สำเร็จ'}
        </p>
      )}
      <Link
        to={`/app/${projectId}/evm`}
        className="mt-3 inline-block text-xs font-semibold text-navy hover:text-gold"
      >
        ดูรายละเอียด EVM →
      </Link>
    </ChartCard>
  )
}
