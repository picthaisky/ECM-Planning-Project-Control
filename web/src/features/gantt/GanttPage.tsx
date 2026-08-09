import { useParams } from 'react-router-dom'
import { GanttChart } from './components/GanttChart'
import { useGanttData } from './useGanttData'

/**
 * S6-FE-01/02/03 "Gantt / CPM" screen (US-6.1, US-6.2): the real, virtualized+canvas Gantt chart
 * (`GET .../gantt`, S6-BE-01).
 *
 * `dataDateIso` comes from the real `GanttDto.dataDate` (`Project.DataDate`) — previously always
 * `null` here (a genuine, flagged backend gap: no reachable endpoint exposed `DataDate` at all).
 * Gap closed by adding `DataDate` directly to `GanttDto`/`GetGanttQueryHandler` — the cheapest of
 * the two options this sprint's frontend report flagged, rather than shipping the heavier
 * `GetProjectQuery`. The rendering mechanism (`GanttChart`'s `dataDateIso` prop,
 * `canvasDraw.ts#drawBody`'s line geometry) was already fully implemented and unit-tested against
 * a supplied date; this was the one-line wiring change that report anticipated.
 */
export function GanttPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const { activities, dataDateIso, loadState, loadError } = useGanttData(projectId ?? '')

  if (!projectId) return null

  if (loadState === 'loading') {
    return (
      <div className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-text-faint">
        กำลังโหลดข้อมูล Gantt...
      </div>
    )
  }

  if (loadState === 'error') {
    return (
      <div role="alert" className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-danger">
        {loadError ?? 'โหลดข้อมูล Gantt ไม่สำเร็จ'}
      </div>
    )
  }

  return <GanttChart activities={activities} dataDateIso={dataDateIso} />
}
