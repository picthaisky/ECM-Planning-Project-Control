import { useParams } from 'react-router-dom'
import { RequireRole } from '../../routes'
import { useEvmSnapshots } from '../evm'
import { useGanttData } from '../gantt'
import { useWbsTree } from '../wbs'
import { DashboardCriticalPathPreview } from './components/DashboardCriticalPathPreview'
import { DashboardKpiRow } from './components/DashboardKpiRow'
import { DashboardPhotoStripPlaceholder } from './components/DashboardPhotoStripPlaceholder'
import { DashboardSCurvePreview } from './components/DashboardSCurvePreview'
import { DashboardWbsRollupCard } from './components/DashboardWbsRollupCard'
import { describeWarning } from './dashboardSelectors'
import { useDashboardData } from './useDashboardData'

/** Roles allowed to view the Executive Dashboard — mirrors `DashboardController`'s own
 * `[Authorize(Roles = "PM,QS,ProjectDirector,Executive,Admin")]` exactly (ADR-0013, same list
 * `EvmController`/`CashFlowController` use). Unlike `features/evm/EvmPage.tsx` (where only the
 * *write* affordance is role-gated because `GET .../evm`'s read is open to any authenticated role —
 * see `AppRoutes.tsx`'s remarks), this endpoint's **read** itself is server-side role-gated, so the
 * whole screen is wrapped in `RequireRole` rather than gating one button — this is a UX affordance
 * only, the server enforces it regardless (`RequireRole`'s own doc comment). */
const DASHBOARD_ROLES = ['PM', 'QS', 'ProjectDirector', 'Executive', 'Admin'] as const

/**
 * S8-FE-01 "Executive Dashboard" screen (US-8.1/US-8.3): `GET .../dashboard` (S8-BE-02) plus three
 * secondary, independently-loaded real datasets the primary response does not itself carry — the
 * S-Curve preview's closed-period history (`GET .../evm/snapshots`), the critical-path preview
 * (`GET .../gantt`), and the WBS rollup card's real branch/weight structure (`GET .../wbs-tree`).
 * Each has its own load/error state (mirrors `features/evm/EvmPage.tsx`'s `useEvmData`/
 * `useEvmSnapshots` split) so one failing secondary fetch degrades only its own widget, never blanks
 * the whole page.
 *
 * Fetching 4 endpoints for one screen is a real, deliberate trade-off — the alternative was
 * fabricating widget content, which this codebase's conventions rule out. Flagged in this sprint's
 * frontend report as a candidate for a dedicated dashboard-aggregate endpoint in a later sprint if
 * this proves too slow in practice, particularly `GET .../gantt`'s "whole project, no pagination"
 * shape (ADR-0004) on a very large schedule.
 */
export function DashboardPage() {
  const { projectId } = useParams<{ projectId: string }>()
  if (!projectId) return null

  return (
    <RequireRole allowedRoles={[...DASHBOARD_ROLES]}>
      <DashboardContent projectId={projectId} />
    </RequireRole>
  )
}

function DashboardContent({ projectId }: { projectId: string }) {
  const dashboardData = useDashboardData(projectId)
  const snapshotsData = useEvmSnapshots(projectId)
  const ganttData = useGanttData(projectId)
  const wbsTreeData = useWbsTree(projectId)

  // Checked *before* the loading gate below — same "error must not also match the loading
  // condition" ordering as `features/evm/EvmPage.tsx` (a real bug caught by that sprint's own
  // integration test): on a genuine load failure `dashboardData.dashboard` stays `null` forever.
  if (dashboardData.loadState === 'error') {
    return (
      <div
        role="alert"
        className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-danger"
      >
        {dashboardData.loadError ?? 'โหลดข้อมูล Dashboard ไม่สำเร็จ'}
      </div>
    )
  }

  if (dashboardData.loadState === 'loading' || !dashboardData.dashboard) {
    return (
      <div className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-text-faint">
        กำลังโหลดข้อมูล Dashboard...
      </div>
    )
  }

  const dashboard = dashboardData.dashboard

  return (
    <div className="flex flex-col gap-4" data-testid="dashboard-page">
      {dashboard.warnings.length > 0 && (
        <div
          role="alert"
          className="rounded-card border border-danger/30 bg-danger/5 px-3 py-2 text-[11px] text-danger"
        >
          {dashboard.warnings.map(describeWarning).join(' · ')}
        </div>
      )}

      <DashboardKpiRow projectId={projectId} dashboard={dashboard} />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.6fr_1fr]">
        <DashboardSCurvePreview
          projectId={projectId}
          dashboard={dashboard}
          snapshots={snapshotsData.snapshots}
          snapshotsLoadState={snapshotsData.loadState}
          snapshotsLoadError={snapshotsData.loadError}
        />
        <DashboardCriticalPathPreview
          projectId={projectId}
          activities={ganttData.activities}
          loadState={ganttData.loadState}
          loadError={ganttData.loadError}
        />
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.6fr_1fr]">
        <DashboardWbsRollupCard
          projectId={projectId}
          rollup={dashboard.progressRollup}
          rootNodes={wbsTreeData.rootNodes}
          loadState={wbsTreeData.loadState}
          loadError={wbsTreeData.loadError}
        />
        <DashboardPhotoStripPlaceholder projectId={projectId} />
      </div>
    </div>
  )
}
