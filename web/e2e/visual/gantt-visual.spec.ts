import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import { loginAndOpenGantt, DEV_USER, PERF_PROJECT_ID } from '../support/gantt'
import { colors } from '../../src/config/theme'
import { BAR_HEIGHT, BAR_TOP, PX_PER_DAY, ROW_HEIGHT, computeTimelineBounds, dateToX } from '../../src/features/gantt/timeScale'
import type { GanttActivityDto } from '../../src/features/gantt/types'

/**
 * S6-QA-02 (docs/10. Sprint 6, qa-engineer row; US-6.1): visual regression vs.
 * `docs/ECM Planning Prototype.dc.html` screen #4 (Gantt / CPM) — critical/non-critical bar
 * coloring, bar layering, and the data-date line's position AND color.
 *
 * The Gantt body is a single `<canvas>` (ADR-0004, "no DOM-per-bar") — there are no per-bar DOM
 * nodes or computed styles to assert against, so this suite reads real pixels back from the real
 * canvas element's own 2D backing store (`getImageData`) in a real browser, at coordinates
 * computed from the same pure geometry functions (`timeScale.ts#dateToX`/`computeTimelineBounds`)
 * production code uses — never eyeballed/screenshot-diffed coordinates. `timeScale.ts` and
 * `config/theme.ts` are both plain, DOM-free TypeScript, so they are imported directly here
 * (same source of truth the app itself uses, not re-derived or hand-copied constants).
 *
 * Design-token ground truth for this screen, cross-checked directly against the prototype
 * markup (not eyeballed):
 *   - `docs/ECM Planning Prototype.dc.html` line 209: critical bar `#B23A3A` (== `colors.danger`)
 *   - line 210: non-critical bar `#33507A` (== `colors.secondary`)
 *   - line 211: baseline swatch `#C9A227` (== `colors.gold`) — legitimately out of scope for pixel
 *     assertions here: no `Baseline` entity/endpoint exists until Sprint 14
 *     (`GanttLegend.tsx`'s own honestly-labeled swatch), so there is no baseline bar to sample.
 *   - line 225 + line 231's caption ("เส้นทองแนวตั้ง = Data date"): the Gantt screen's own
 *     data-date indicator is a **gold** (`rgba(201,162,39,...)` == `#C9A227`) vertical line, NOT
 *     navy — confirmed independently by the `.claude/skills/cmplus-ui/SKILL.md` screen-list line
 *     ("data-date **gold** vertical line") — this is a different rule from the *EVM S-Curve*
 *     screen's own data-date line, which the same prototype file (line 262) genuinely does draw
 *     in navy (`#0F2542`). The two screens are NOT the same rule; this suite tests the Gantt
 *     screen specifically.
 */

function hexToRgb(hex: string): [number, number, number] {
  const clean = hex.replace('#', '')
  return [parseInt(clean.slice(0, 2), 16), parseInt(clean.slice(2, 4), 16), parseInt(clean.slice(4, 6), 16)]
}

function distance(a: readonly [number, number, number], b: readonly [number, number, number]): number {
  return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2])
}

async function fetchGanttData(page: Page, projectId: string): Promise<{ activities: GanttActivityDto[]; dataDate: string }> {
  const loginRes = await page.request.post('/api/v1/auth/login', { data: DEV_USER })
  expect(loginRes.ok()).toBe(true)
  const { accessToken } = (await loginRes.json()) as { accessToken: string }

  const ganttRes = await page.request.get(`/api/v1/projects/${projectId}/gantt`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  expect(ganttRes.ok()).toBe(true)
  return ganttRes.json()
}

async function scrollGanttBody(page: Page, { top, left }: { top?: number; left?: number }): Promise<void> {
  await page.evaluate(
    ({ top, left }) => {
      const canvas = document.querySelector('[data-testid="gantt-body-canvas"]') as HTMLCanvasElement
      const container = canvas.closest('.overflow-auto') as HTMLElement
      if (top !== undefined) container.scrollTop = top
      if (left !== undefined) container.scrollLeft = left
    },
    { top, left },
  )
  // scroll -> native 'scroll' event -> GanttChart.tsx's rAF-batched redraw; give it a couple of
  // frames to actually repaint before sampling pixels.
  await page.waitForTimeout(150)
}

/** Reads one CSS-pixel coordinate back from the canvas's real backing store, accounting for
 * `devicePixelRatio` the same way `GanttChart.tsx#sizeCanvasForDpr` scales the canvas itself. */
async function samplePixel(page: Page, cssX: number, cssY: number): Promise<[number, number, number]> {
  return page.evaluate(
    ({ cssX, cssY }) => {
      const canvas = document.querySelector('[data-testid="gantt-body-canvas"]') as HTMLCanvasElement
      const ctx = canvas.getContext('2d')!
      const dpr = window.devicePixelRatio || 1
      const data = ctx.getImageData(Math.round(cssX * dpr), Math.round(cssY * dpr), 1, 1).data
      return [data[0], data[1], data[2]] as [number, number, number]
    },
    { cssX, cssY },
  )
}

/** Scans a small column band around `expectedCssX` (the geometrically-computed data-date line
 * position) across the visible viewport height, looking for a pixel closely matching one of the
 * two candidate colors (gold per the prototype, navy per the current implementation). Bar fills
 * (danger/secondary) are far in color-space from both candidates, so they can't produce a false
 * match even though the vertical line is drawn (deliberately, see `canvasDraw.ts#drawBody`) after
 * — i.e. on top of — any bar it crosses. Returns the candidate name, sampled RGB, and its distance
 * to that candidate, or `null` if neither candidate color was found near that column at all
 * (meaning the line isn't where the geometry says it should be — also a real, reportable finding).
 */
async function findDataDateLineColor(
  page: Page,
  expectedCssX: number,
  viewportCssHeight: number,
): Promise<{ candidate: 'gold' | 'navy'; rgb: [number, number, number]; dist: number } | null> {
  const gold = hexToRgb(colors.gold)
  const navy = hexToRgb(colors.navy)
  const TOLERANCE = 20

  let best: { candidate: 'gold' | 'navy'; rgb: [number, number, number]; dist: number } | null = null

  for (let dx = -3; dx <= 3; dx += 1) {
    for (let y = 0; y < viewportCssHeight; y += 2) {
      const rgb = await samplePixel(page, expectedCssX + dx, y)
      const goldDist = distance(rgb, gold)
      const navyDist = distance(rgb, navy)
      const [candidate, dist] = goldDist <= navyDist ? (['gold', goldDist] as const) : (['navy', navyDist] as const)
      if (dist < TOLERANCE && (!best || dist < best.dist)) {
        best = { candidate, rgb, dist }
      }
    }
  }

  return best
}

test.describe('S6-QA-02: Gantt visual regression vs. prototype (docs/ECM Planning Prototype.dc.html #4)', () => {
  test('critical bars render danger/red and non-critical bars render secondary/slate-blue, at their geometrically-correct x position', async ({
    page,
  }) => {
    const gantt = await fetchGanttData(page, PERF_PROJECT_ID)
    const bounds = computeTimelineBounds(gantt.activities)
    const pxPerDay = PX_PER_DAY.week // GanttChart.tsx's hardcoded initial zoom

    await loginAndOpenGantt(page, PERF_PROJECT_ID)

    // --- Critical bar: real dataset's row 0 ("Steel Erection", isCritical=true; verified via this
    // sprint's own direct API check), no scroll needed. ---
    const criticalActivity = gantt.activities[0]
    expect(criticalActivity.isCritical).toBe(true)
    const critX1 = dateToX(criticalActivity.plannedStart, bounds.start, pxPerDay)
    const critX2 = dateToX(criticalActivity.plannedFinish, bounds.start, pxPerDay)
    const critMidX = (Math.min(critX1, critX2) + Math.max(critX1, critX2)) / 2
    const critMidY = BAR_TOP + BAR_HEIGHT / 2

    const criticalRgb = await samplePixel(page, critMidX, critMidY)
    expect(distance(criticalRgb, hexToRgb(colors.danger))).toBeLessThan(6)

    // --- Non-critical bar: real dataset's row 699 ("Plumbing Rough-in", isCritical=false — one of
    // only 15 non-critical activities in this 10,000-row seed; verified directly). Scroll it into
    // view first. ---
    const nonCriticalIndex = gantt.activities.findIndex((a) => !a.isCritical)
    expect(nonCriticalIndex).toBeGreaterThan(-1)
    const nonCriticalActivity = gantt.activities[nonCriticalIndex]

    const rowTop = nonCriticalIndex * ROW_HEIGHT
    const scrollTop = Math.max(0, rowTop - 20)
    await scrollGanttBody(page, { top: scrollTop })

    const nonCritX1 = dateToX(nonCriticalActivity.plannedStart, bounds.start, pxPerDay)
    const nonCritX2 = dateToX(nonCriticalActivity.plannedFinish, bounds.start, pxPerDay)
    const nonCritMidX = (Math.min(nonCritX1, nonCritX2) + Math.max(nonCritX1, nonCritX2)) / 2
    const nonCritMidY = (rowTop - scrollTop) + BAR_TOP + BAR_HEIGHT / 2

    const nonCriticalRgb = await samplePixel(page, nonCritMidX, nonCritMidY)
    expect(distance(nonCriticalRgb, hexToRgb(colors.secondary))).toBeLessThan(6)
  })

  test('the data-date vertical line is positioned at the geometrically-correct x and colored to match the prototype (gold, not navy)', async ({
    page,
  }) => {
    const gantt = await fetchGanttData(page, PERF_PROJECT_ID)
    const bounds = computeTimelineBounds(gantt.activities)
    const pxPerDay = PX_PER_DAY.week

    await loginAndOpenGantt(page, PERF_PROJECT_ID)

    const expectedAbsoluteX = dateToX(gantt.dataDate, bounds.start, pxPerDay)
    // Bring the data-date column into view near the left edge of the viewport.
    const scrollLeft = Math.max(0, Math.round(expectedAbsoluteX - 150))
    await scrollGanttBody(page, { left: scrollLeft, top: 0 })
    const expectedCssX = expectedAbsoluteX - scrollLeft

    const found = await findDataDateLineColor(page, expectedCssX, 500)

    // Real, attachable evidence for this finding either way.
    console.log(
      `[S6-QA-02] data-date line: expectedAbsoluteX=${expectedAbsoluteX.toFixed(1)} scrollLeft=${scrollLeft} ` +
        `expectedCssX=${expectedCssX.toFixed(1)} found=${found ? `${found.candidate} rgb(${found.rgb.join(',')}) dist=${found.dist.toFixed(1)}` : 'none'}`,
    )

    expect(found).not.toBeNull()
    // Per the prototype (source of truth, ADR-0006) this must be gold (`#C9A227`), matching the
    // Gantt screen's own caption "เส้นทองแนวตั้ง = Data date" and the `/cmplus-ui` skill's screen-list
    // line — NOT the navy currently drawn by `canvasDraw.ts#drawBody` (`ctx.strokeStyle =
    // colors.navy`), which is this suite's real, reportable finding (see this sprint's QA report).
    expect(found?.candidate).toBe('gold')
  })

  test('the chart chrome (header, legend, zoom control) matches its saved baseline screenshot', async ({ page }) => {
    // Second, complementary regression layer to the geometric pixel-sampling above (per the DoD's
    // "Playwright's built-in toHaveScreenshot() ... or a more targeted pixel/color-sampling check"
    // — this suite does both). Deliberately scoped to the deterministic *header chrome* (title,
    // legend swatches, zoom control) rather than the full 10,000-row body: the body's bar layout
    // is already covered precisely (and more informatively on failure) by the two tests above, and
    // a full-body screenshot diff would be far more prone to incidental cross-machine
    // font/subpixel noise for no extra coverage.
    await loginAndOpenGantt(page, PERF_PROJECT_ID)
    const header = page.locator('div').filter({ hasText: 'Gantt Chart — CPM Schedule' }).first()
    await expect(header).toHaveScreenshot('gantt-header-chrome.png')
  })
})
