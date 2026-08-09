import { Button, StatTile, StatusPill } from '../../../components'
import type { useRecalculateCpm } from '../useRecalculateCpm'

export interface CriticalPathPreviewProps {
  cpm: ReturnType<typeof useRecalculateCpm>
}

/**
 * S5-FE-01 (US-5.1): the "คำนวณ CPM ใหม่" button plus a critical-path preview for the WBS &
 * Activity screen — a preview/summary, not the Gantt chart itself (Sprint 6's `web/src/features/
 * gantt/`, per docs/10 §7's own scope split). Driven entirely by `useRecalculateCpm` (passed in as
 * `cpm`, same "dumb component driven by a hook's return shape" pattern as `ProgressUpdatePanel`
 * taking `form={useBatchProgressForm(...)}`), so this component has no API/fetch logic of its own
 * and can be unit-tested with a hand-built `cpm` object.
 *
 * `RecalculateCpmResultDto` (`POST .../cpm/recalculate`, S5-BE-04) has no per-activity code/name —
 * only `ActivityId` values (`criticalPath: Guid[]`), since the CPM engine itself only ever dealt in
 * ids (see `CpmActivityInput`'s own doc comment on why it stays decoupled from the full `Activity`
 * entity). There is also no project-wide "activity by id" read endpoint yet (`GetNodeActivitiesQuery`
 * is scoped per WBS node, not global) to enrich these with a code/name without adding N lookups this
 * sprint didn't spec. Showing the raw id, numbered in schedule order, is therefore the honest
 * DoD-meeting choice — "รายการ critical path เรียงตามลำดับกิจกรรม" (S5-FE-01 DoD) requires the
 * *order* to be correct, not a friendlier label; Sprint 6's Gantt is where activity code/name +
 * critical highlighting are shown together on the real timeline.
 */
export function CriticalPathPreview({ cpm }: CriticalPathPreviewProps) {
  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-heading text-sm font-semibold text-navy">Critical Path (CPM)</h3>
          <p className="mt-0.5 text-[10.5px] text-text-faint">
            คำนวณเส้นทางวิกฤตใหม่จากโครงข่ายกิจกรรมและความสัมพันธ์ (Activity Relation) ทั้งหมดของโครงการ
          </p>
        </div>

        <div className="flex items-center gap-2">
          {cpm.state === 'success' && <StatusPill label="คำนวณสำเร็จ" tone="success" />}
          {cpm.state === 'error' && <StatusPill label="คำนวณไม่สำเร็จ" tone="danger" />}
          <Button size="sm" loading={cpm.state === 'calculating'} onClick={() => void cpm.recalculate()}>
            คำนวณ CPM ใหม่
          </Button>
        </div>
      </div>

      {cpm.state === 'calculating' && (
        <p role="status" className="mt-3 text-xs text-text-faint">
          กำลังคำนวณเส้นทางวิกฤต (Critical Path)...
        </p>
      )}

      {cpm.state === 'error' && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {cpm.error}
        </p>
      )}

      {cpm.state === 'success' && cpm.result && (
        <div className="mt-4">
          <div className="grid grid-cols-3 gap-3">
            <StatTile label="กิจกรรมทั้งหมด" value={cpm.result.activitiesProcessed.toLocaleString('th-TH')} />
            <StatTile
              label="กิจกรรมวิกฤต (Critical)"
              value={cpm.result.criticalActivityCount.toLocaleString('th-TH')}
              tone="danger"
            />
            <StatTile
              label="ระยะเวลาโครงการ (วัน)"
              value={cpm.result.projectDurationDays.toLocaleString('th-TH')}
            />
          </div>

          <p className="mt-4 text-[11px] font-medium uppercase tracking-wide text-text-faint">
            ลำดับกิจกรรมวิกฤต (Critical Path) — เรียงตามลำดับกำหนดการ
          </p>

          {cpm.result.criticalPath.length === 0 ? (
            <p className="mt-2 text-xs text-text-faint">
              ไม่พบกิจกรรมวิกฤตในโครงข่ายนี้ (ทุกกิจกรรมมี Total Float มากกว่า 0)
            </p>
          ) : (
            <ol className="mt-2 max-h-72 divide-y divide-border-subtle overflow-y-auto rounded-card border border-border">
              {cpm.result.criticalPath.map((activityId, index) => (
                <li key={activityId} className="flex items-center gap-3 px-3 py-2 text-xs">
                  <span
                    aria-hidden="true"
                    className="flex h-5 w-5 flex-none items-center justify-center rounded-full bg-danger/10 text-[10px] font-semibold text-danger"
                  >
                    {index + 1}
                  </span>
                  <span className="truncate font-mono text-text">{activityId}</span>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </div>
  )
}
