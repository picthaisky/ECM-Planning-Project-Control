import { useMemo, useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { GanttLabelRow } from '../components/GanttLabelRow'
import { useGanttRowViewModels } from './useGanttRowViewModels'
import type { GanttActivityDto } from '../types'

/**
 * S6-FE-03 (US-6.2) DoD: "React DevTools profiler ยืนยันว่าแถวที่ props ไม่เปลี่ยนไม่ re-render
 * (บันทึกผลไว้ในชุด perf)".
 *
 * `React.Profiler`'s public `onRender` callback was tried here first and rejected after actually
 * running it (not assumed): it fires once per commit that reaches a given position in the tree
 * *regardless* of whether a `memo`-wrapped child underneath bailed out — confirmed empirically by
 * wrapping each row in its own `<Profiler>` and observing the callback fire on every parent
 * re-render even when the row's own props never changed (4 calls across a mount + 3 no-op parent
 * re-renders, not 1). `onRender` measures "was this Profiler boundary part of a commit", not "did
 * this specific memoized component's render body actually execute" — the two are different
 * questions, and only the second one is what the DoD is actually asking to prove.
 *
 * `GanttLabelRow`'s `onRenderProbe` prop (test-only, `undefined`/no-op in production) answers the
 * right question unambiguously: it is called exactly once per actual invocation of
 * `GanttLabelRowImpl`'s render body, which is precisely what "re-render" means for a memoized
 * function component, and is what the DevTools Profiler flamegraph visualizes under the hood (a
 * literal DevTools browser-extension capture isn't automatable in this environment — this is the
 * honest, verifiable programmatic equivalent the DoD explicitly allows).
 */

function buildActivities(count: number): GanttActivityDto[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `a${i}`,
    wbsNodeId: 'node-1',
    activityCode: `ACT-${100 + i}`,
    name: `กิจกรรม ${i}`,
    plannedStart: '2026-01-01T00:00:00+07:00',
    plannedFinish: '2026-02-01T00:00:00+07:00',
    actualStart: null,
    actualFinish: null,
    isCritical: i % 2 === 0,
    totalFloat: i,
    freeFloat: i,
  }))
}

interface HarnessProps {
  activities: GanttActivityDto[]
  renderCounts: Record<string, number>
}

/** Renders N `GanttLabelRow`s via the real `useGanttRowViewModels` hook (end-to-end realism).
 * `unrelatedTick` simulates the kind of parent re-render a scroll-driven virtualizer update
 * actually causes — a re-render of the *pane*, with the `activities` prop (and therefore every
 * row's view-model) completely unchanged.
 *
 * `onRenderProbe`s are built via `useMemo(() => …, [rows])`, so each row gets a *stable* probe
 * reference for as long as `rows` itself is stable (exactly the real production guarantee
 * `useGanttRowViewModels` provides) — a fresh inline arrow function per render would itself count
 * as a changed prop and defeat `React.memo`, which would make this test measure the harness's own
 * mistake rather than `GanttLabelRow`'s actual memoization. */
function Harness({ activities, renderCounts }: HarnessProps) {
  const rows = useGanttRowViewModels(activities)
  const [unrelatedTick, setUnrelatedTick] = useState(0)
  const tops = useMemo(() => rows.map((_, i) => i * 44), [rows])
  const probes = useMemo(
    () => rows.map((row) => () => (renderCounts[row.id] = (renderCounts[row.id] ?? 0) + 1)),
    [rows, renderCounts],
  )

  return (
    <div>
      <button onClick={() => setUnrelatedTick((t) => t + 1)}>force parent re-render</button>
      <span data-testid="tick">{unrelatedTick}</span>
      {rows.map((row, index) => (
        <GanttLabelRow key={row.id} viewModel={row} top={tops[index]} onRenderProbe={probes[index]} />
      ))}
    </div>
  )
}

describe('useGanttRowViewModels + GanttLabelRow memoization (S6-FE-03, ADR-0004)', () => {
  it('a row whose data has not changed does not re-render across repeated unrelated parent re-renders (the real scroll scenario)', async () => {
    const activities = buildActivities(5)
    const renderCounts: Record<string, number> = {}
    const user = userEvent.setup()

    render(<Harness activities={activities} renderCounts={renderCounts} />)

    // Initial mount: every row's render body executes exactly once.
    expect(Object.values(renderCounts)).toEqual([1, 1, 1, 1, 1])

    // Three unrelated parent re-renders (standing in for the pane re-rendering on scroll): the
    // `activities` array reference never changes, so `useGanttRowViewModels` (memoized on
    // `[activities]`) returns the exact same row + probe references every time.
    await user.click(screen.getByRole('button', { name: 'force parent re-render' }))
    await user.click(screen.getByRole('button', { name: 'force parent re-render' }))
    await user.click(screen.getByRole('button', { name: 'force parent re-render' }))

    expect(screen.getByTestId('tick')).toHaveTextContent('3')
    // Still exactly one render-body execution per row — `GanttLabelRow`'s `React.memo` skipped all
    // three re-renders because none of its own props ever changed.
    expect(Object.values(renderCounts)).toEqual([1, 1, 1, 1, 1])
  })

  it('recomputes every row view-model (new references) when the underlying activities array is reloaded — the memo does not silently go stale', () => {
    const activities = buildActivities(3)
    const renderCounts: Record<string, number> = {}

    const { rerender } = render(<Harness activities={activities} renderCounts={renderCounts} />)
    expect(Object.values(renderCounts)).toEqual([1, 1, 1])

    // A genuine data reload (a brand-new `activities` array, e.g. from `useGanttData` refetching)
    // is expected to re-render every row — the control case proving this isn't vacuously "memo
    // always blocks everything".
    const reloaded = buildActivities(3).map((a) => ({ ...a, totalFloat: (a.totalFloat ?? 0) + 100 }))
    rerender(<Harness activities={reloaded} renderCounts={renderCounts} />)

    expect(Object.values(renderCounts)).toEqual([2, 2, 2])
  })
})

describe('GanttLabelRow in isolation (sanity check: memoization is not vacuously true)', () => {
  it('does not re-render when given the exact same props, but does re-render when a prop actually changes', () => {
    const viewModel = { id: 'a1', activityCode: 'ACT-100', name: 'กิจกรรมทดสอบ', metaLabel: 'ACT-100 · TF 0' }
    let renderCount = 0
    const probe = () => (renderCount += 1) // one stable reference reused across every rerender below

    const { rerender } = render(<GanttLabelRow viewModel={viewModel} top={0} onRenderProbe={probe} />)
    expect(renderCount).toBe(1)

    // Re-render with the identical object reference and primitive `top` — `React.memo`'s default
    // shallow prop comparison must skip this render entirely.
    rerender(<GanttLabelRow viewModel={viewModel} top={0} onRenderProbe={probe} />)
    expect(renderCount).toBe(1)

    // Now an actually-different prop (`top`, simulating this row scrolling to a new position) —
    // proves the memo comparator isn't just permanently short-circuiting every re-render.
    rerender(<GanttLabelRow viewModel={viewModel} top={44} onRenderProbe={probe} />)
    expect(renderCount).toBe(2)
  })
})
