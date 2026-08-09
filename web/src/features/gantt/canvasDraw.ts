import { colors } from '../../config/theme'
import { BAR_HEIGHT, BAR_TOP, HEADER_HEIGHT, ROW_HEIGHT, dateToX, getHeaderTicks } from './timeScale'
import type { TimelineBounds, ZoomLevel } from './timeScale'

/**
 * Imperative canvas drawing for the Gantt (ADR-0004: "bars drawn on a canvas/single-SVG layer …
 * no DOM-per-bar designs will pass review"). Every function here draws only the current viewport
 * slice (`viewportWidth`/`viewportHeight`) of a chart that may represent 10,000+ activities and a
 * multi-year timeline — the canvas element itself is always viewport-sized (see
 * `components/GanttChart.tsx`'s "sticky small canvas over a huge scrollable sizer div" layout), so
 * redraw cost scales with what's on screen, never with total row/day count.
 *
 * Deliberately framework-free (`ctx: CanvasRenderingContext2D`-shaped parameter, not a real
 * `HTMLCanvasElement`) so these are unit-testable against a plain mock object under jsdom, which
 * has no real canvas 2D backend — see `canvasDraw.test.ts`.
 */

/** Minimal structural subset of `CanvasRenderingContext2D` this module actually calls — lets tests
 * pass a plain `vi.fn()`-based mock instead of needing a real browser canvas backend. */
export interface DrawingContext {
  save(): void
  restore(): void
  clearRect(x: number, y: number, w: number, h: number): void
  fillRect(x: number, y: number, w: number, h: number): void
  beginPath(): void
  moveTo(x: number, y: number): void
  lineTo(x: number, y: number): void
  stroke(): void
  fill(): void
  fillText(text: string, x: number, y: number): void
  setLineDash(segments: number[]): void
  roundRect?(x: number, y: number, w: number, h: number, radius: number): void
  // `string | CanvasGradient | CanvasPattern` matches `CanvasRenderingContext2D`'s own real type
  // (never narrowed to `string`) so a real canvas 2D context is directly assignable to this
  // interface — this module only ever assigns plain color strings, never a gradient/pattern.
  fillStyle: string | CanvasGradient | CanvasPattern
  strokeStyle: string | CanvasGradient | CanvasPattern
  lineWidth: number
  font: string
  textAlign: CanvasTextAlign
  textBaseline: CanvasTextBaseline
}

/** Draws a filled rectangle with rounded corners, falling back to a plain `fillRect` when the
 * context has no `roundRect` (older browser) — cosmetic only, never a correctness issue for bar
 * geometry itself. */
function fillRoundedRect(ctx: DrawingContext, x: number, y: number, w: number, h: number, radius: number): void {
  ctx.beginPath()
  if (typeof ctx.roundRect === 'function') {
    ctx.roundRect(x, y, w, h, radius)
  } else {
    ctx.fillRect(x, y, w, h)
    return
  }
  ctx.fill()
}

export interface GanttBarRow {
  id: string
  plannedStart: string
  plannedFinish: string
  actualStart: string | null
  actualFinish: string | null
  isCritical: boolean
}

export interface DrawBodyParams {
  ctx: DrawingContext
  rows: readonly GanttBarRow[]
  scrollTop: number
  scrollLeft: number
  viewportWidth: number
  viewportHeight: number
  timelineStart: Date
  pxPerDay: number
  /** `Project.DataDate`, when available — currently always `null` in production; see
   * `GanttPage.tsx`'s remarks on the missing backend DTO field. The vertical line is only ever
   * drawn when this is non-null and its computed x-position falls within the visible viewport. */
  dataDateIso: string | null
}

/** Draws every visible row's divider + bar (+ actual-span overlay) for the current scroll
 * position, plus the data-date line on top. Only rows whose index range intersects
 * `[scrollTop, scrollTop + viewportHeight]` are visited (a couple of arithmetic comparisons per
 * row in view, not per row in the dataset) — this is the "virtualization" half of the bar layer,
 * complementing the DOM-side row virtualization in the label pane. */
export function drawBody(params: DrawBodyParams): void {
  const { ctx, rows, scrollTop, scrollLeft, viewportWidth, viewportHeight, timelineStart, pxPerDay, dataDateIso } =
    params

  ctx.save()
  ctx.clearRect(0, 0, viewportWidth, viewportHeight)
  ctx.fillStyle = colors.surface
  ctx.fillRect(0, 0, viewportWidth, viewportHeight)

  const firstIndex = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - 2)
  const lastIndex = Math.min(rows.length - 1, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + 2)

  for (let i = firstIndex; i <= lastIndex; i += 1) {
    const row = rows[i]
    if (!row) continue

    const rowTop = i * ROW_HEIGHT - scrollTop

    ctx.strokeStyle = colors.borderSubtle
    ctx.lineWidth = 1
    ctx.beginPath()
    ctx.moveTo(0, rowTop)
    ctx.lineTo(viewportWidth, rowTop)
    ctx.stroke()

    const plannedX1 = dateToX(row.plannedStart, timelineStart, pxPerDay) - scrollLeft
    const plannedX2 = dateToX(row.plannedFinish, timelineStart, pxPerDay) - scrollLeft
    const barX = Math.min(plannedX1, plannedX2)
    const barWidth = Math.max(Math.abs(plannedX2 - plannedX1), 2)

    if (barX + barWidth < 0 || barX > viewportWidth) continue

    const barY = rowTop + BAR_TOP
    ctx.fillStyle = row.isCritical ? colors.danger : colors.secondary
    fillRoundedRect(ctx, barX, barY, barWidth, BAR_HEIGHT, 3)

    // Actual-progress overlay (GanttActivityDto.ActualStart/ActualFinish) — only drawn once both
    // ends of the actual span are recorded, clipped to the planned bar's own rectangle. This is a
    // deliberate adaptation from the prototype's mock "% complete" translucent overlay: the real
    // DTO carries actual *dates*, not a progress percentage (see `types.ts`'s remarks), so the
    // overlay here represents "how the recorded actual span compares to the planned span" rather
    // than a literal percent-complete fill.
    if (row.actualStart && row.actualFinish) {
      const actualX1 = dateToX(row.actualStart, timelineStart, pxPerDay) - scrollLeft
      const actualX2 = dateToX(row.actualFinish, timelineStart, pxPerDay) - scrollLeft
      const overlayLeft = Math.max(barX, Math.min(actualX1, actualX2))
      const overlayRight = Math.min(barX + barWidth, Math.max(actualX1, actualX2))
      const overlayWidth = overlayRight - overlayLeft
      if (overlayWidth > 0) {
        ctx.fillStyle = 'rgba(255,255,255,0.35)'
        fillRoundedRect(ctx, overlayLeft, barY, overlayWidth, BAR_HEIGHT, 3)
      }
    }
  }

  if (dataDateIso) {
    const x = dateToX(dataDateIso, timelineStart, pxPerDay) - scrollLeft
    if (x >= 0 && x <= viewportWidth) {
      ctx.save()
      ctx.strokeStyle = colors.navy
      ctx.lineWidth = 1
      ctx.setLineDash([4, 3])
      ctx.beginPath()
      ctx.moveTo(x + 0.5, 0)
      ctx.lineTo(x + 0.5, viewportHeight)
      ctx.stroke()
      ctx.restore()
    }
  }

  ctx.restore()
}

export interface DrawHeaderParams {
  ctx: DrawingContext
  zoom: ZoomLevel
  bounds: TimelineBounds
  pxPerDay: number
  scrollLeft: number
  viewportWidth: number
}

/** Draws the time-axis tick strip (day/week/month labels, per `getHeaderTicks`). A separate,
 * small, fixed-height canvas from `drawBody` (see `GanttChart.tsx`) redrawn on horizontal scroll
 * only — ticks are bounded by timeline day-span, never by activity count, and are drawn on canvas
 * rather than as DOM nodes for the same "no per-item DOM node at scale" reasoning as the bars. */
export function drawHeader(params: DrawHeaderParams): void {
  const { ctx, zoom, bounds, pxPerDay, scrollLeft, viewportWidth } = params

  ctx.save()
  ctx.clearRect(0, 0, viewportWidth, HEADER_HEIGHT)
  ctx.fillStyle = colors.tableHeaderBg
  ctx.fillRect(0, 0, viewportWidth, HEADER_HEIGHT)

  const ticks = getHeaderTicks(zoom, bounds, pxPerDay)
  ctx.font = '9.5px "IBM Plex Sans Thai", sans-serif'
  ctx.textAlign = 'left'
  ctx.textBaseline = 'middle'

  for (const tick of ticks) {
    const x = tick.x - scrollLeft
    if (x < -80 || x > viewportWidth + 80) continue

    ctx.strokeStyle = colors.borderSubtle
    ctx.lineWidth = 1
    ctx.beginPath()
    ctx.moveTo(x + 0.5, 0)
    ctx.lineTo(x + 0.5, HEADER_HEIGHT)
    ctx.stroke()

    ctx.fillStyle = tick.isMajor ? colors.navy : colors.textFaint
    ctx.fillText(tick.label, x + 4, HEADER_HEIGHT / 2 + 1)
  }

  ctx.restore()
}
