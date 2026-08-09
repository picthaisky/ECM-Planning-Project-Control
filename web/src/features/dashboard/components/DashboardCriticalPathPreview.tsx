import { Link } from 'react-router-dom'
import { StatusPill } from '../../../components'
import type { GanttActivityDto } from '../../gantt'
import { selectTopCriticalActivities } from '../dashboardSelectors'

export interface DashboardCriticalPathPreviewProps {
  projectId: string
  activities: GanttActivityDto[]
  loadState: 'loading' | 'ready' | 'error'
  loadError: string | null
}

const FINISH_DATE_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'Asia/Bangkok',
})

function formatFinish(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? '—' : FINISH_DATE_FORMATTER.format(parsed)
}

/**
 * S8-FE-01's "Critical Path — งานวิกฤตใกล้ถึงกำหนด" preview card (prototype row 2, right column).
 * `DashboardResponseDto` carries no schedule/critical-path data at all — this reuses the real
 * `GET .../gantt` read (Sprint 6's CPM output: `isCritical`/`totalFloat`/`plannedFinish`), filtered
 * and sorted client-side by `selectTopCriticalActivities` (pure, unit-tested). There is no
 * dashboard-specific critical-path endpoint; adding one would be a backend change out of scope for
 * this frontend-only slice, and fetching the whole Gantt just for a 4-row preview is a real,
 * flagged performance trade-off on a very large schedule (see this sprint's frontend report) — the
 * only honest alternative to fabricating this widget's content.
 */
export function DashboardCriticalPathPreview({
  projectId,
  activities,
  loadState,
  loadError,
}: DashboardCriticalPathPreviewProps) {
  const topCritical = selectTopCriticalActivities(activities)

  return (
    <div className="flex h-full flex-col rounded-card border border-border bg-surface p-4">
      <h3 className="font-heading text-sm font-semibold text-navy">Critical Path — งานวิกฤตใกล้ถึงกำหนด</h3>

      {loadState === 'loading' && (
        <p role="status" className="mt-3 text-xs text-text-faint">
          กำลังโหลดข้อมูล Critical Path...
        </p>
      )}

      {loadState === 'error' && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {loadError ?? 'โหลดข้อมูล Critical Path ไม่สำเร็จ'}
        </p>
      )}

      {loadState === 'ready' && topCritical.length === 0 && (
        <p className="mt-3 text-xs text-text-faint">
          ยังไม่พบกิจกรรมวิกฤต (อาจยังไม่เคยคำนวณ CPM หรือยังไม่มีกิจกรรมในโครงการ)
        </p>
      )}

      {loadState === 'ready' && topCritical.length > 0 && (
        <ul className="mt-3 flex flex-col gap-2.5 text-[11.5px]">
          {topCritical.map((activity) => (
            <li key={activity.id} className="grid grid-cols-[1fr_auto] items-center gap-2">
              <div className="min-w-0">
                <div className="truncate font-medium text-text">{activity.name}</div>
                <div className="truncate text-[10.5px] text-text-faint">
                  {activity.activityCode} · กำหนดเสร็จ {formatFinish(activity.plannedFinish)}
                </div>
              </div>
              <StatusPill
                label={`TF=${activity.totalFloat ?? '—'}`}
                tone="danger"
                className="flex-none"
              />
            </li>
          ))}
        </ul>
      )}

      <Link
        to={`/app/${projectId}/gantt`}
        className="mt-auto pt-3 text-xs font-semibold text-navy hover:text-gold"
      >
        ดู Gantt Chart ทั้งหมด →
      </Link>
    </div>
  )
}
