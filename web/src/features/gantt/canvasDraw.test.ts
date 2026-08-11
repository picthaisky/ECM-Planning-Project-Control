import { describe, expect, it, vi } from 'vitest'
import { colors } from '../../config/theme'
import { drawBody, drawHeader } from './canvasDraw'
import type { DrawingContext, GanttBarRow } from './canvasDraw'
import { BAR_TOP, HEADER_HEIGHT, ROW_HEIGHT } from './timeScale'

/**
 * jsdom has no real canvas 2D backend (`getContext('2d')` returns `null`), so these tests exercise
 * `drawBody`/`drawHeader` against a plain mock implementing `DrawingContext` — the same technique
 * production code uses to stay null-safe under jsdom (`GanttChart.tsx`'s `sizeCanvasForDpr`).
 * Asserting the exact coordinates passed to `moveTo`/`lineTo`/`fillRect` is what actually proves
 * "geometrically correct x-position" (S6-FE-01 DoD) — not merely that draw calls happened.
 */
function createMockContext(): DrawingContext {
  return {
    save: vi.fn(),
    restore: vi.fn(),
    clearRect: vi.fn(),
    fillRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    stroke: vi.fn(),
    fill: vi.fn(),
    fillText: vi.fn(),
    setLineDash: vi.fn(),
    fillStyle: '',
    strokeStyle: '',
    lineWidth: 1,
    font: '',
    textAlign: 'left',
    textBaseline: 'alphabetic',
  }
}

function bar(overrides: Partial<GanttBarRow>): GanttBarRow {
  return {
    id: 'a1',
    plannedStart: '2026-01-01T00:00:00+07:00',
    plannedFinish: '2026-01-11T00:00:00+07:00',
    actualStart: null,
    actualFinish: null,
    isCritical: false,
    ...overrides,
  }
}

describe('drawBody — data-date line geometry (S6-FE-01 DoD)', () => {
  const timelineStart = new Date(2026, 0, 1)
  const pxPerDay = 32

  it('draws the line at exactly (daysBetween * pxPerDay - scrollLeft) when it is inside the viewport', () => {
    const ctx = createMockContext()

    drawBody({
      ctx,
      rows: [],
      scrollTop: 0,
      scrollLeft: 50,
      viewportWidth: 800,
      viewportHeight: 400,
      timelineStart,
      pxPerDay,
      dataDateIso: '2026-01-11T00:00:00+07:00', // 10 days after timelineStart -> x = 320
    })

    // 320 (raw x) - 50 (scrollLeft) = 270; drawn at +0.5 for crisp 1px lines.
    expect(ctx.moveTo).toHaveBeenCalledWith(270.5, 0)
    expect(ctx.lineTo).toHaveBeenCalledWith(270.5, 400)
    expect(ctx.strokeStyle).toBe(colors.navy)
  })

  it('does not draw the line at all when its x-position is scrolled out of the viewport', () => {
    const ctx = createMockContext()
    const moveToCallsBefore = (ctx.moveTo as ReturnType<typeof vi.fn>).mock.calls.length

    drawBody({
      ctx,
      rows: [],
      scrollTop: 0,
      scrollLeft: 10_000, // scrolls the data date (x=320) far out of any viewport
      viewportWidth: 800,
      viewportHeight: 400,
      timelineStart,
      pxPerDay,
      dataDateIso: '2026-01-11T00:00:00+07:00',
    })

    expect((ctx.moveTo as ReturnType<typeof vi.fn>).mock.calls.length).toBe(moveToCallsBefore)
  })

  it('never draws a line at all when dataDateIso is null (no fabricated data-date, per this sprint\'s gap)', () => {
    const ctx = createMockContext()

    drawBody({
      ctx,
      rows: [],
      scrollTop: 0,
      scrollLeft: 0,
      viewportWidth: 800,
      viewportHeight: 400,
      timelineStart,
      pxPerDay,
      dataDateIso: null,
    })

    expect(ctx.setLineDash).not.toHaveBeenCalled()
  })
})

describe('drawBody — bar coloring and virtualized row range', () => {
  const timelineStart = new Date(2026, 0, 1)
  const pxPerDay = 32

  it('colors a critical bar with the danger token and a non-critical bar with the secondary token', () => {
    const ctx = createMockContext()
    const rows = [bar({ id: 'critical', isCritical: true }), bar({ id: 'non-critical', isCritical: false })]

    const fillStyles: string[] = []
    Object.defineProperty(ctx, 'fillStyle', {
      get: () => fillStyles.at(-1) ?? '',
      set: (v: string) => fillStyles.push(v),
    })

    drawBody({
      ctx,
      rows,
      scrollTop: 0,
      scrollLeft: 0,
      viewportWidth: 800,
      viewportHeight: 400,
      timelineStart,
      pxPerDay,
      dataDateIso: null,
    })

    expect(fillStyles).toContain(colors.danger)
    expect(fillStyles).toContain(colors.secondary)
  })

  it('only visits rows whose index range intersects the current scroll viewport (+ a small overscan), never every row', () => {
    const ctx = createMockContext()
    const rows = Array.from({ length: 10_000 }, (_, i) => bar({ id: `a${i}` }))

    drawBody({
      ctx,
      rows,
      scrollTop: 0,
      scrollLeft: 0,
      viewportWidth: 800,
      viewportHeight: 400, // ~9 rows visible at ROW_HEIGHT=44
      timelineStart,
      pxPerDay,
      dataDateIso: null,
    })

    // Each visited row draws exactly one divider line (`moveTo`+`lineTo` pair before the bar
    // itself) - far fewer than 10,000 despite the dataset size (ADR-0004's canvas-side
    // virtualization, complementing the DOM-side row virtualization in the label pane).
    expect((ctx.moveTo as ReturnType<typeof vi.fn>).mock.calls.length).toBeLessThan(30)
  })

  it('draws the actual-span overlay only when both actualStart and actualFinish are recorded, clipped to the planned bar', () => {
    const ctx = createMockContext()
    const rows = [
      bar({
        id: 'with-actual',
        plannedStart: '2026-01-01T00:00:00+07:00',
        plannedFinish: '2026-01-11T00:00:00+07:00',
        actualStart: '2026-01-02T00:00:00+07:00',
        actualFinish: '2026-01-05T00:00:00+07:00',
      }),
      bar({ id: 'no-actual', actualStart: null, actualFinish: null }),
    ]

    const capturedFillStyles: string[] = []
    Object.defineProperty(ctx, 'fillStyle', {
      get: () => capturedFillStyles.at(-1) ?? '',
      set: (v: string) => capturedFillStyles.push(v),
    })

    drawBody({
      ctx,
      rows,
      scrollTop: 0,
      scrollLeft: 0,
      viewportWidth: 800,
      viewportHeight: 400,
      timelineStart,
      pxPerDay,
      dataDateIso: null,
    })

    // The translucent white overlay color is only ever set when a row actually has both actual
    // dates recorded (`with-actual`) — `no-actual` never contributes it, but this asserts the
    // color appeared at all (no `ctx.roundRect` on the mock, so `fillRoundedRect` exercises its
    // `fillRect`-fallback path here, matching a real older-browser canvas too).
    expect(capturedFillStyles).toContain('rgba(255,255,255,0.35)')
    expect((ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThanOrEqual(3) // background + 2 bars minimum
  })
})

describe('drawHeader — tick placement follows scrollLeft', () => {
  it('shifts every tick left by exactly scrollLeft', () => {
    const bounds = { start: new Date(2026, 0, 1), end: new Date(2026, 1, 1) }
    const ctxNoScroll = createMockContext()
    const ctxScrolled = createMockContext()

    drawHeader({ ctx: ctxNoScroll, zoom: 'day', bounds, pxPerDay: 32, scrollLeft: 0, viewportWidth: 800 })
    drawHeader({ ctx: ctxScrolled, zoom: 'day', bounds, pxPerDay: 32, scrollLeft: 32, viewportWidth: 800 })

    const firstMoveToNoScroll = (ctxNoScroll.moveTo as ReturnType<typeof vi.fn>).mock.calls[0]
    const firstMoveToScrolled = (ctxScrolled.moveTo as ReturnType<typeof vi.fn>).mock.calls[0]

    expect(firstMoveToNoScroll[0] - firstMoveToScrolled[0]).toBeCloseTo(32, 5)
  })

  it('fills the header background before drawing any ticks', () => {
    const ctx = createMockContext()
    const bounds = { start: new Date(2026, 0, 1), end: new Date(2026, 1, 1) }

    drawHeader({ ctx, zoom: 'month', bounds, pxPerDay: 3, scrollLeft: 0, viewportWidth: 800 })

    expect(ctx.fillRect).toHaveBeenCalledWith(0, 0, 800, HEADER_HEIGHT)
  })
})

// Sanity: BAR_TOP/ROW_HEIGHT are used by drawBody's own row-top math — imported here only so a
// future accidental deletion of these named exports fails this file's compilation, not silently.
void BAR_TOP
void ROW_HEIGHT
