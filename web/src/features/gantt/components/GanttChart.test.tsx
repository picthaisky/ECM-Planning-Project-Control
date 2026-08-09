import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { GanttChart } from './GanttChart'
import type { GanttActivityDto } from '../types'

// jsdom performs no real layout, so a scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the viewport) default to 0 — stub a realistic viewport,
// exactly like DataTable.test.tsx/WbsTreeGrid.test.tsx already establish for the same reason.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth')
})

function buildActivities(count: number): GanttActivityDto[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `a${i}`,
    wbsNodeId: 'node-1',
    activityCode: `ACT-${100 + i}`,
    name: `กิจกรรม ${i}`,
    plannedStart: `2026-01-${String((i % 27) + 1).padStart(2, '0')}T00:00:00+07:00`,
    plannedFinish: `2026-02-${String((i % 27) + 1).padStart(2, '0')}T00:00:00+07:00`,
    actualStart: null,
    actualFinish: null,
    isCritical: i % 3 === 0,
    totalFloat: i % 3 === 0 ? 0 : i,
    freeFloat: i,
  }))
}

describe('GanttChart', () => {
  it('renders the legend, zoom control, and both canvases (header + body) for the bar layer', () => {
    render(<GanttChart activities={buildActivities(5)} dataDateIso={null} />)

    expect(screen.getByText('Critical (TF=0)')).toBeInTheDocument()
    expect(screen.getByText('Non-critical')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'ระดับการซูม' })).toBeInTheDocument()
    expect(screen.getByTestId('gantt-header-canvas').tagName).toBe('CANVAS')
    expect(screen.getByTestId('gantt-body-canvas').tagName).toBe('CANVAS')
  })

  it('ADR-0004 (S6-FE-01 DoD, "ไม่มีดีไซน์ DOM-per-bar"): bars are drawn on canvas — 10,000 activities never produce 10,000 DOM bar elements, and there are still only 2 <canvas> elements total', () => {
    const activities = buildActivities(10_000)
    const { container } = render(<GanttChart activities={activities} dataDateIso={null} />)

    expect(container.querySelectorAll('canvas').length).toBe(2)
    // No per-bar DOM node exists anywhere in the tree for the bar layer — bars are canvas-only.
    expect(container.querySelectorAll('[data-gantt-bar]').length).toBe(0)
  })

  it('ADR-0004: the virtualized label pane mounts only the visible window + overscan, never one DOM row per activity', () => {
    const activities = buildActivities(10_000)
    const { container } = render(<GanttChart activities={activities} dataDateIso={null} />)

    const renderedLabelRows = container.querySelectorAll('[data-gantt-row]')

    expect(renderedLabelRows.length).toBeGreaterThan(0)
    expect(renderedLabelRows.length).toBeLessThan(60)
    expect(renderedLabelRows.length).toBeLessThan(activities.length)
  })

  it('shows an honest empty state instead of an empty chart shell when the project has zero activities', () => {
    render(<GanttChart activities={[]} dataDateIso={null} />)
    expect(screen.getByText('ไม่มีกิจกรรมสำหรับโครงการนี้')).toBeInTheDocument()
    expect(screen.queryByTestId('gantt-body-canvas')).not.toBeInTheDocument()
  })

  it('states honestly that the data-date line has no real data yet, rather than fabricating a date', () => {
    render(<GanttChart activities={buildActivities(3)} dataDateIso={null} />)
    expect(screen.getByText(/ยังไม่มีข้อมูล Data Date จาก Project/)).toBeInTheDocument()
  })

  it('shows the real formatted data date caption once a value is actually supplied', () => {
    render(<GanttChart activities={buildActivities(3)} dataDateIso="2026-07-11T00:00:00+07:00" />)
    expect(screen.getByText(/Data date \(11 กรกฎาคม 2569\)/)).toBeInTheDocument()
  })

  it('S6-FE-02: three zoom levels are offered, default is week, and clicking switches the active level', async () => {
    const user = userEvent.setup()
    const { container } = render(<GanttChart activities={buildActivities(20)} dataDateIso={null} />)

    expect(container.querySelector('[data-gantt-zoom="week"]')).toBeInTheDocument()

    const dayButton = screen.getByRole('button', { name: 'วัน' })
    const weekButton = screen.getByRole('button', { name: 'สัปดาห์' })
    const monthButton = screen.getByRole('button', { name: 'เดือน' })
    expect(weekButton).toHaveAttribute('aria-pressed', 'true')
    expect(dayButton).toHaveAttribute('aria-pressed', 'false')

    await user.click(monthButton)

    expect(container.querySelector('[data-gantt-zoom="month"]')).toBeInTheDocument()
    expect(monthButton).toHaveAttribute('aria-pressed', 'true')
    expect(weekButton).toHaveAttribute('aria-pressed', 'false')
  })

  it('does not crash when canvas 2D context is unavailable (jsdom has none) — draw becomes a no-op, never a throw', () => {
    // jsdom's own `getContext` already returns null by default and logs a "not implemented"
    // console error; explicitly stubbing it here makes that behavior the actual, asserted
    // contract for this test rather than an incidental side effect of the test environment.
    const getContextSpy = vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)

    expect(() => render(<GanttChart activities={buildActivities(50)} dataDateIso={null} />)).not.toThrow()

    getContextSpy.mockRestore()
  })
})
