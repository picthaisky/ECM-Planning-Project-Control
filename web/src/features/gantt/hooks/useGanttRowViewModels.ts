import { useMemo } from 'react'
import type { GanttActivityDto } from '../types'

/** The exact, flattened set of fields `GanttLabelRow` renders — a deliberately small, all-primitive
 * shape (never the raw `GanttActivityDto` object) so `React.memo`'s default shallow prop comparison
 * on the row component actually works: passing primitives means "props unchanged" is a real,
 * per-field `Object.is` check, not a reference check on the whole DTO. */
export interface GanttRowViewModel {
  id: string
  activityCode: string
  name: string
  /** e.g. `"ACT-214 · TF 0"` / `"ACT-260 · TF —"` — matches the prototype's Gantt row meta line
   * (`docs/ECM Planning Prototype.dc.html`'s `g.meta`, e.g. `'ACT-214 · TF 0 · ช้า 3 วัน'`); the
   * "ช้า N วัน" delay suffix isn't reproduced since the real DTO carries no schedule-variance-in-
   * days field to compute it from without guessing. */
  metaLabel: string
}

function formatMetaLabel(activity: GanttActivityDto): string {
  const tf = activity.totalFloat === null ? '—' : activity.totalFloat.toLocaleString('th-TH')
  return `${activity.activityCode} · TF ${tf}`
}

/**
 * S6-FE-03 (US-6.2): maps the raw `GanttActivityDto[]` fetched once per page load into the stable,
 * primitive-only view-model `GanttLabelRow` actually renders. Memoized on `[activities]` alone, so
 * the returned array — and every individual row object inside it — keeps the *same reference*
 * across any re-render that isn't a genuine data reload (a zoom-level change, a scroll-driven
 * virtualizer re-render, an unrelated parent state update). That stability is what lets
 * `GanttLabelRow`'s `React.memo` actually skip re-rendering an already-mounted, unchanged row —
 * proven in `useGanttRowViewModels.test.tsx` via a `React.Profiler`-based render-count assertion
 * (ADR-0004's "row props stable/memoized" requirement).
 */
export function useGanttRowViewModels(activities: readonly GanttActivityDto[]): GanttRowViewModel[] {
  return useMemo(
    () =>
      activities.map((activity) => ({
        id: activity.id,
        activityCode: activity.activityCode,
        name: activity.name,
        metaLabel: formatMetaLabel(activity),
      })),
    [activities],
  )
}
