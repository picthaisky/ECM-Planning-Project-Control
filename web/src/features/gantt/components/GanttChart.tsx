import { useCallback, useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { drawBody, drawHeader } from '../canvasDraw'
import type { GanttBarRow } from '../canvasDraw'
import { useGanttRowViewModels } from '../hooks/useGanttRowViewModels'
import { useGanttZoom } from '../hooks/useGanttZoom'
import { useSyncedVerticalScroll } from '../hooks/useSyncedVerticalScroll'
import {
  HEADER_HEIGHT,
  LABEL_PANE_WIDTH,
  PX_PER_DAY,
  ROW_HEIGHT,
  computeTimelineBounds,
  totalContentWidth,
} from '../timeScale'
import type { GanttActivityDto } from '../types'
import { GanttLabelRow } from './GanttLabelRow'
import { GanttLegend } from './GanttLegend'
import { ZoomControl } from './ZoomControl'

export interface GanttChartProps {
  activities: readonly GanttActivityDto[]
  /**
   * `Project.DataDate` — the ISO date the gold/navy vertical data-date line marks (US-6.1's third
   * acceptance criterion). Always `null` today: neither `GanttDto` nor the `ProjectDto` the
   * frontend can actually reach exposes this field, even though `Project.DataDate` exists on the
   * backend domain entity — see `GanttPage.tsx`'s remarks for the full gap description and the
   * concrete fix. This prop exists (rather than the line being hardcoded absent) precisely so
   * wiring in the real value later is a one-line change at the call site, not a rewrite here — the
   * geometry (`canvasDraw.ts#drawBody`) is fully implemented and unit-tested against a supplied
   * date already.
   */
  dataDateIso: string | null
  /** Visible body-viewport height in px (both scroll panes). Matches `DataTable`'s own `height`
   * prop convention. */
  height?: number
}

const dataDateCaptionFormatter = new Intl.DateTimeFormat('th-TH', { dateStyle: 'long', timeZone: 'Asia/Bangkok' })

function sizeCanvasForDpr(canvas: HTMLCanvasElement, cssWidth: number, cssHeight: number, dpr: number): void {
  const targetWidth = Math.max(1, Math.round(cssWidth * dpr))
  const targetHeight = Math.max(1, Math.round(cssHeight * dpr))
  if (canvas.width !== targetWidth) canvas.width = targetWidth
  if (canvas.height !== targetHeight) canvas.height = targetHeight
  canvas.style.width = `${cssWidth}px`
  canvas.style.height = `${cssHeight}px`

  const ctx = canvas.getContext('2d')
  // `null` under jsdom (no real canvas 2D backend) and in browsers with canvas disabled — every
  // caller of this helper tolerates that (draw becomes a harmless no-op, never a crash).
  ctx?.setTransform(dpr, 0, 0, dpr, 0, 0)
}

/**
 * S6-FE-01/02/03 (US-6.1/6.2): the Gantt chart. Two-pane layout — a virtualized DOM list of
 * activity labels (left, `@tanstack/react-virtual`, the same library/technique `DataTable.tsx`
 * already established, ADR-0004) and a single canvas-drawn bar layer (right) — never one DOM node
 * per bar, regardless of activity count.
 *
 * Rendering architecture (right pane): a small, viewport-sized `<canvas>` positioned
 * `position: sticky; top: 0; left: 0` inside a much larger, non-canvas "sizer" div whose explicit
 * `width`/`height` equal the full timeline/row-count content size. The sizer div is what gives the
 * browser's native scrollbars the correct proportional range; the canvas itself is redrawn
 * imperatively (never via React state) on every native `scroll`/resize event, using
 * `requestAnimationFrame` batching, so a continuous scroll gesture costs one small canvas repaint
 * per frame — never a re-render of the row/bar tree (`/cmplus-ui`'s "scroll/zoom state outside
 * React (refs) to avoid re-render storms").
 *
 * Row indentation: deliberately **not** modeled. `GanttActivityDto.wbsNodeId` correlates each bar
 * to its WBS node, but the prototype's actual Gantt screen (`docs/ECM Planning Prototype.dc.html`
 * lines 220-224, screen #4) renders a flat two-line label (name + meta) with no indent/caret —
 * unlike the WBS tree screen's own indented rows. Since visual fidelity to the prototype is this
 * sprint's DoD (S6-QA-02), no WBS-tree depth correlation was built; this is a resolved modeling
 * decision, not an outstanding gap.
 */
export function GanttChart({ activities, dataDateIso, height = 520 }: GanttChartProps) {
  const leftScrollRef = useRef<HTMLDivElement>(null)
  const rightScrollRef = useRef<HTMLDivElement>(null)
  const headerCanvasRef = useRef<HTMLCanvasElement>(null)
  const bodyCanvasRef = useRef<HTMLCanvasElement>(null)
  const drawRef = useRef<() => void>(() => {})
  const rafRef = useRef<number | null>(null)

  const { zoom, changeZoom } = useGanttZoom(rightScrollRef, 'week')
  const rows = useGanttRowViewModels(activities)

  const barRows: GanttBarRow[] = useMemo(
    () =>
      activities.map((activity) => ({
        id: activity.id,
        plannedStart: activity.plannedStart,
        plannedFinish: activity.plannedFinish,
        actualStart: activity.actualStart,
        actualFinish: activity.actualFinish,
        isCritical: activity.isCritical,
      })),
    [activities],
  )

  const bounds = useMemo(() => computeTimelineBounds(activities), [activities])
  const pxPerDay = PX_PER_DAY[zoom]
  const totalWidth = useMemo(() => totalContentWidth(bounds, pxPerDay), [bounds, pxPerDay])
  const totalHeight = rows.length * ROW_HEIGHT

  useSyncedVerticalScroll(leftScrollRef, rightScrollRef)

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => leftScrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 10,
  })

  const draw = useCallback(() => {
    const right = rightScrollRef.current
    const bodyCanvas = bodyCanvasRef.current
    const headerCanvas = headerCanvasRef.current
    if (!right || !bodyCanvas || !headerCanvas) return

    const viewportWidth = right.clientWidth
    const viewportHeight = right.clientHeight
    const dpr = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1

    sizeCanvasForDpr(bodyCanvas, viewportWidth, viewportHeight, dpr)
    sizeCanvasForDpr(headerCanvas, viewportWidth, HEADER_HEIGHT, dpr)

    const bodyCtx = bodyCanvas.getContext('2d')
    const headerCtx = headerCanvas.getContext('2d')
    if (!bodyCtx || !headerCtx) return // jsdom / canvas-unsupported environment — no-op, never a crash

    drawBody({
      ctx: bodyCtx,
      rows: barRows,
      scrollTop: right.scrollTop,
      scrollLeft: right.scrollLeft,
      viewportWidth,
      viewportHeight,
      timelineStart: bounds.start,
      pxPerDay,
      dataDateIso,
    })

    drawHeader({
      ctx: headerCtx,
      zoom,
      bounds,
      pxPerDay,
      scrollLeft: right.scrollLeft,
      viewportWidth,
    })
  }, [barRows, bounds, pxPerDay, dataDateIso, zoom])

  // Keeps `drawRef` pointing at the freshest closure after every render (the "latest ref" pattern)
  // so the scroll/resize listeners attached once below never read stale `bounds`/`pxPerDay`/zoom.
  useLayoutEffect(() => {
    drawRef.current = draw
  })

  const scheduleDraw = useCallback(() => {
    if (rafRef.current !== null) return
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      drawRef.current()
    })
  }, [])

  // Attached once per mount: `scheduleDraw` only ever touches refs, so it never needs to be
  // re-bound when data/zoom changes (those are picked up via `drawRef`, updated above).
  useEffect(() => {
    const right = rightScrollRef.current
    if (!right) return undefined

    right.addEventListener('scroll', scheduleDraw, { passive: true })

    let resizeObserver: ResizeObserver | undefined
    if (typeof ResizeObserver !== 'undefined') {
      resizeObserver = new ResizeObserver(scheduleDraw)
      resizeObserver.observe(right)
    }

    return () => {
      right.removeEventListener('scroll', scheduleDraw)
      resizeObserver?.disconnect()
      if (rafRef.current !== null) {
        cancelAnimationFrame(rafRef.current)
        rafRef.current = null
      }
    }
  }, [scheduleDraw])

  // Redraws immediately on a genuine data/zoom/data-date change (not only on the next
  // scroll/resize event) — e.g. right after activities finish loading, or right after the user
  // picks a new zoom level (in addition to `useGanttZoom`'s own scrollLeft correction).
  useEffect(() => {
    scheduleDraw()
  }, [scheduleDraw, barRows, bounds, pxPerDay, dataDateIso, zoom])

  const dataDateCaption = dataDateIso
    ? `เส้นประสีกรมท่าแนวตั้ง = Data date (${dataDateCaptionFormatter.format(new Date(dataDateIso))})`
    : 'ยังไม่มีข้อมูล Data Date จาก Project (Project.DataDate ยังไม่ถูกส่งผ่าน API ใด ๆ ที่หน้านี้เรียกใช้ได้จริง — ดูรายละเอียดในรายงานสปรินต์)'

  return (
    <div className="overflow-hidden rounded-card border border-border bg-surface" data-gantt-zoom={zoom}>
      <div className="flex items-center gap-2.5 border-b border-border-subtle px-4 py-3">
        <span className="font-heading text-[13.5px] font-semibold text-navy">Gantt Chart — CPM Schedule</span>
        <GanttLegend />
        <ZoomControl zoom={zoom} onChange={changeZoom} />
      </div>

      {rows.length === 0 ? (
        <div className="flex items-center justify-center py-10 text-xs text-text-faint">
          ไม่มีกิจกรรมสำหรับโครงการนี้
        </div>
      ) : (
        <>
          <div className="flex text-[11.5px]">
            <div
              style={{ width: LABEL_PANE_WIDTH, height: HEADER_HEIGHT }}
              className="flex flex-none items-center border-b border-r border-border-subtle bg-surface-muted px-4 text-[10.5px] text-text-faint"
            >
              กิจกรรม
            </div>
            <div style={{ height: HEADER_HEIGHT }} className="flex-1 overflow-hidden border-b border-border-subtle">
              <canvas ref={headerCanvasRef} style={{ display: 'block' }} data-testid="gantt-header-canvas" />
            </div>
          </div>

          <div className="flex" style={{ height }}>
            <div
              ref={leftScrollRef}
              style={{ width: LABEL_PANE_WIDTH }}
              className="flex-none overflow-y-auto overflow-x-hidden border-r border-border-subtle"
            >
              <div style={{ height: totalHeight, position: 'relative' }}>
                {virtualizer.getVirtualItems().map((virtualRow) => (
                  <GanttLabelRow
                    key={rows[virtualRow.index].id}
                    viewModel={rows[virtualRow.index]}
                    top={virtualRow.start}
                  />
                ))}
              </div>
            </div>

            <div ref={rightScrollRef} className="relative flex-1 overflow-auto">
              <div style={{ width: totalWidth, height: totalHeight, position: 'relative' }}>
                <canvas
                  ref={bodyCanvasRef}
                  style={{ position: 'sticky', top: 0, left: 0, display: 'block' }}
                  data-testid="gantt-body-canvas"
                />
              </div>
            </div>
          </div>
        </>
      )}

      <div className="border-t border-border-subtle px-4 py-2.5 text-[10.5px] text-text-faint">
        {dataDateCaption}
        {' · '}แถบขาวโปร่งบนบาร์ = ช่วงวันที่บันทึกจริง (Actual Start–Finish)
      </div>
    </div>
  )
}
