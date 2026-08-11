/**
 * Wire shapes for S6-BE-01's Gantt read (`GetGanttQuery` -> `GanttDto`/`GanttActivityDto`,
 * `backend/src/CMPlus.Application/Features/Gantt/Queries/GetGantt/GanttDto.cs`).
 *
 * Every `DateTimeOffset`/`DateTimeOffset?` field is a plain ISO-8601 string on the wire (System.Text.Json
 * default — unlike `decimal`/`decimal?`, which go through `DecimalAsStringJsonConverter`; there are no
 * money/percent fields in this DTO at all, deliberately — see the backend doc comment on
 * `GanttActivityDto`: "lean … no ProgressPercentage, no budget/cost fields, no WBS title/weight").
 *
 * `wbsNodeId` is carried through but intentionally unused for indentation/grouping in this sprint's
 * rendering — see `components/GanttChart.tsx`'s remarks for why (the prototype's actual Gantt screen
 * renders a flat activity list with no per-row indent, unlike the WBS tree screen).
 */
export interface GanttActivityDto {
  id: string
  wbsNodeId: string
  activityCode: string
  name: string
  /** Bar start on the planned/current schedule. */
  plannedStart: string
  /** Bar end on the planned/current schedule. */
  plannedFinish: string
  /** Actual-progress overlay start, if recorded. `null` until progress has been recorded against
   * this activity. */
  actualStart: string | null
  /** Actual-progress overlay end, if recorded. `null` until the activity is marked complete. */
  actualFinish: string | null
  /** Critical (danger/red) vs non-critical (secondary/slate-blue) bar coloring — written by the
   * Sprint 5 CPM engine (`RecalculateCpmCommand`); `false` until CPM has run at least once. */
  isCritical: boolean
  /** Days of total float; `null` until CPM has run at least once for this project. */
  totalFloat: number | null
  /** Days of free float; `null` until CPM has run at least once for this project. */
  freeFloat: number | null
}

/** `GET /api/v1/projects/{projectId}/gantt` response body (`GanttDto`) — activities already in
 * WBS-tree row order (own activities before child-node activities, siblings by code); the frontend
 * renders this array top-to-bottom directly, it never re-sorts or re-nests it. */
export interface GanttDto {
  projectId: string
  /** `Project.DataDate` — positions the Gantt's vertical data-date line (gap closure: previously
   * absent from every reachable endpoint; see `GanttPage.tsx`'s prior doc comment, now resolved). */
  dataDate: string
  activities: GanttActivityDto[]
}
