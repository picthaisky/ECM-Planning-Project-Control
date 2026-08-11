import { memo } from 'react'
import type { GanttRowViewModel } from '../hooks/useGanttRowViewModels'
import { ROW_HEIGHT } from '../timeScale'

export interface GanttLabelRowProps {
  viewModel: GanttRowViewModel
  /** `rowIndex * ROW_HEIGHT` — a `translateY` offset, matching `DataTable.tsx`'s own absolute-
   * positioned virtualized row technique (ADR-0004) rather than a fourth positioning convention. */
  top: number
  /**
   * Test-only render probe, called synchronously once per actual invocation of this component's
   * render body — `undefined`/no-op in production. This is the one reliable way to prove "this
   * memoized row's render function did/did not execute": `React.Profiler`'s public `onRender`
   * callback was tried first and empirically fires once per *commit that reaches this position in
   * the tree* regardless of whether `memo` bailed the child out underneath it (verified directly —
   * see `hooks/useGanttRowViewModels.test.tsx`'s comment and this sprint's frontend report), so it
   * cannot by itself distinguish "re-rendered" from "bailed out". Counting actual render-body
   * executions here is unambiguous and is what the DevTools Profiler's flamegraph visualizes under
   * the hood (a real DevTools capture isn't automatable in this environment; this is the honest,
   * verifiable programmatic equivalent, S6-FE-03 DoD).
   */
  onRenderProbe?: () => void
}

function GanttLabelRowImpl({ viewModel, top, onRenderProbe }: GanttLabelRowProps) {
  onRenderProbe?.()

  return (
    <div
      data-gantt-row={viewModel.id}
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: ROW_HEIGHT,
        transform: `translateY(${top}px)`,
      }}
      className="flex flex-col justify-center gap-0.5 border-t border-border-subtle px-4"
    >
      <span className="truncate text-[12px] font-medium text-text">{viewModel.name}</span>
      <span className="truncate text-[10px] text-text-faint">{viewModel.metaLabel}</span>
    </div>
  )
}

/**
 * ADR-0004 / S6-FE-03 (US-6.2): memoized with stable, all-primitive props. `useGanttRowViewModels`
 * keeps `viewModel` referentially unchanged across any re-render that isn't a genuine activity-data
 * reload, so a row already mounted when the label pane re-renders (e.g. on every scroll-driven
 * virtualizer update) is skipped by this `memo`, not re-rendered. Proven — not merely asserted — in
 * `hooks/useGanttRowViewModels.test.tsx` via the `onRenderProbe` render-count assertion.
 */
export const GanttLabelRow = memo(GanttLabelRowImpl)
